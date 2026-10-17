using System;
using System.Threading.Tasks;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PdfReader.App.Rendering;
using PdfReader.App.ViewModels;
using PdfReader.Core;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace PdfReader.App;

public sealed partial class MainWindow : Window
{
    private const double PageItemBottomMargin = 16.0;
    private static readonly TimeSpan s_zoomSettleDelay = TimeSpan.FromMilliseconds(180);

    private readonly ViewportRenderCoordinator _viewportCoordinator;
    private readonly DispatcherQueueTimer _zoomDebounceTimer;
    private readonly PointerEventHandler _panPointerPressedHandler;
    private readonly PointerEventHandler _panPointerMovedHandler;
    private readonly PointerEventHandler _panPointerReleasedHandler;
    private readonly PointerEventHandler _panPointerCanceledHandler;
    private ScrollViewer? _scrollViewer;
    private Point _panStartPosition;
    private double _panStartHorizontalOffset;
    private double _panStartVerticalOffset;
    private bool _isPanning;
    private bool _xamlRootSubscribed;
    private bool _isZoomSettling;

    public MainWindow()
    {
        var documentService = new PdfDocumentService();
        var renderPipeline = new PageRenderPipeline(documentService);
        ViewModel = new MainViewModel(documentService, renderPipeline, PickPdfFileAsync);
        _viewportCoordinator = new ViewportRenderCoordinator(renderPipeline, DispatcherQueue);

        _zoomDebounceTimer = DispatcherQueue.CreateTimer();
        _zoomDebounceTimer.Interval = s_zoomSettleDelay;
        _zoomDebounceTimer.IsRepeating = false;
        _zoomDebounceTimer.Tick += OnZoomDebounceTimerTick;

        _panPointerPressedHandler = new PointerEventHandler(OnPagesListViewPointerPressed);
        _panPointerMovedHandler = new PointerEventHandler(OnPagesListViewPointerMoved);
        _panPointerReleasedHandler = new PointerEventHandler(OnPagesListViewPointerReleased);
        _panPointerCanceledHandler = new PointerEventHandler(OnPagesListViewPointerCanceled);

        InitializeComponent();

        PagesListView.AddHandler(UIElement.PointerPressedEvent, _panPointerPressedHandler, handledEventsToo: true);
        PagesListView.AddHandler(UIElement.PointerMovedEvent, _panPointerMovedHandler, handledEventsToo: true);
        PagesListView.AddHandler(UIElement.PointerReleasedEvent, _panPointerReleasedHandler, handledEventsToo: true);
        PagesListView.AddHandler(UIElement.PointerCanceledEvent, _panPointerCanceledHandler, handledEventsToo: true);

        ViewModel.DocumentLoaded += OnDocumentLoaded;
        ViewModel.ReadingThemeChanged += OnReadingThemeChanged;
        ViewModel.UiThemeChanged += OnUiThemeChanged;
        ViewModel.ZoomChanged += OnViewModelZoomChanged;
        ViewModel.ScrollToPageRequested += OnScrollToPageRequested;
        ViewModel.FocusSearchBoxRequested += OnFocusSearchBoxRequested;
        RootGrid.Loaded += OnRootGridLoaded;
        PagesListView.Loaded += OnPagesListViewLoaded;
        PagesListView.SizeChanged += OnPagesListViewSizeChanged;
        Closed += OnWindowClosed;
    }

    public MainViewModel ViewModel { get; }

    private void OnRootGridLoaded(object sender, RoutedEventArgs e)
    {
        if (!_xamlRootSubscribed && RootGrid.XamlRoot is not null)
        {
            _xamlRootSubscribed = true;
            _viewportCoordinator.SetRasterizationScale(RootGrid.XamlRoot.RasterizationScale);
            RootGrid.XamlRoot.Changed += OnXamlRootChanged;
        }
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        _viewportCoordinator.SetRasterizationScale(sender.RasterizationScale);
        RefreshViewport(triggerRenders: true);
    }

    private void OnPagesListViewLoaded(object sender, RoutedEventArgs e)
    {
        EnsureScrollViewerHooked();
        ViewModel.UpdateViewportDimensions(PagesListView.ActualWidth, PagesListView.ActualHeight);
        RefreshViewport(triggerRenders: true);
    }

    private void OnPagesListViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        EnsureScrollViewerHooked();
        ViewModel.UpdateViewportDimensions(e.NewSize.Width, e.NewSize.Height);
        RefreshViewport(triggerRenders: !_isZoomSettling);
    }

    private void OnDocumentLoaded(object? sender, EventArgs e)
    {
        EnsureScrollViewerHooked();
        _scrollViewer?.ChangeView(null, 0, null, disableAnimation: true);
        DispatcherQueue.TryEnqueue(() => RefreshViewport(triggerRenders: true));
    }

    private void OnReadingThemeChanged(object? sender, EventArgs e)
    {
        _viewportCoordinator.CancelAll();
        DispatcherQueue.TryEnqueue(() => RefreshViewport(triggerRenders: true));
    }

    private void OnUiThemeChanged(object? sender, EventArgs e)
    {
        RootPage.RequestedTheme = ViewModel.RootRequestedTheme;
    }

    private void OnViewModelZoomChanged(object? sender, EventArgs e)
    {
        // Keep scaled placeholder bitmaps visible while zoom settles, then re-render at new scale.
        _isZoomSettling = true;
        _viewportCoordinator.CancelAll();
        _zoomDebounceTimer.Stop();
        _zoomDebounceTimer.Start();
    }

    private void OnZoomDebounceTimerTick(DispatcherQueueTimer sender, object args)
    {
        _isZoomSettling = false;
        RefreshViewport(triggerRenders: true);
    }

    private void OnScrollToPageRequested(object? sender, int targetPageIndex)
    {
        EnsureScrollViewerHooked();
        double targetOffset = ViewportRenderCoordinator.CalculatePageVerticalOffset(
            ViewModel.Pages,
            targetPageIndex,
            PageItemBottomMargin);

        DispatcherQueue.TryEnqueue(() =>
        {
            _scrollViewer?.ChangeView(null, targetOffset, null, disableAnimation: true);
            RefreshViewport(triggerRenders: true);
        });
    }

    private void OnFocusSearchBoxRequested(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            SearchQueryTextBox.Focus(FocusState.Programmatic);
            SearchQueryTextBox.SelectAll();
        });
    }

    private void OnScrollViewerViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        RefreshViewport(triggerRenders: !_isZoomSettling);
    }

    private void OnPagesListViewPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!ViewModel.HasDocument || ViewModel.IsTextSelectionMode)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source && IsInsideScrollBar(source))
        {
            return;
        }

        PointerPoint point = e.GetCurrentPoint(PagesListView);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        EnsureScrollViewerHooked();
        if (_scrollViewer is null)
        {
            return;
        }

        _panStartPosition = point.Position;
        _panStartHorizontalOffset = _scrollViewer.HorizontalOffset;
        _panStartVerticalOffset = _scrollViewer.VerticalOffset;
        _isPanning = true;
        PagesListView.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnPagesListViewPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        if (_scrollViewer is null || ViewModel.IsTextSelectionMode)
        {
            EndPanning(e.Pointer);
            return;
        }

        PointerPoint point = e.GetCurrentPoint(PagesListView);
        if (!point.Properties.IsLeftButtonPressed)
        {
            EndPanning(e.Pointer);
            return;
        }

        Point currentPosition = point.Position;
        double deltaX = currentPosition.X - _panStartPosition.X;
        double deltaY = currentPosition.Y - _panStartPosition.Y;

        double targetHorizontalOffset = Math.Clamp(_panStartHorizontalOffset - deltaX, 0.0, _scrollViewer.ScrollableWidth);
        double targetVerticalOffset = Math.Clamp(_panStartVerticalOffset - deltaY, 0.0, _scrollViewer.ScrollableHeight);

        _scrollViewer.ChangeView(targetHorizontalOffset, targetVerticalOffset, null, disableAnimation: true);
        e.Handled = true;
    }

    private void OnPagesListViewPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        EndPanning(e.Pointer);
        e.Handled = true;
    }

    private void OnPagesListViewPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }

        EndPanning(e.Pointer);
    }

    private void EndPanning(Pointer pointer)
    {
        _isPanning = false;
        PagesListView.ReleasePointerCapture(pointer);
    }

    private void OnPagesListViewPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control))
        {
            return;
        }

        PointerPoint point = e.GetCurrentPoint(PagesListView);
        int delta = point.Properties.MouseWheelDelta;
        if (delta != 0)
        {
            e.Handled = true;
            ViewModel.StepZoomFromWheel(delta);
        }
    }

    private void OnGoToPageTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            ViewModel.GoToPageCommand.Execute(null);
        }
    }

    private async void OnSearchQueryTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            await ViewModel.ExecuteSearchCommand.ExecuteAsync(null);
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            ViewModel.CloseSearchBarCommand.Execute(null);
        }
    }

    private async void OnRecentFileItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is RecentFileItem item)
        {
            await ViewModel.OpenRecentFileCommand.ExecuteAsync(item);
        }
    }

    private void EnsureScrollViewerHooked()
    {
        if (_scrollViewer is not null)
        {
            return;
        }

        _scrollViewer = FindDescendant<ScrollViewer>(PagesListView);
        if (_scrollViewer is not null)
        {
            _scrollViewer.ViewChanged += OnScrollViewerViewChanged;
        }
    }

    private void RefreshViewport(bool triggerRenders)
    {
        if (RootGrid.XamlRoot is not null)
        {
            _viewportCoordinator.SetRasterizationScale(RootGrid.XamlRoot.RasterizationScale);
        }

        double verticalOffset = _scrollViewer?.VerticalOffset ?? 0.0;
        double viewportHeight = _scrollViewer?.ViewportHeight > 0
            ? _scrollViewer.ViewportHeight
            : PagesListView.ActualHeight;

        int activePageIndex = _viewportCoordinator.UpdateViewport(
            ViewModel.Pages,
            verticalOffset,
            viewportHeight,
            PageItemBottomMargin,
            triggerRenders);

        if (ViewModel.HasDocument)
        {
            ViewModel.UpdateVisiblePageFromScroll(activePageIndex);
        }
    }

    private async Task<string?> PickPdfFileAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List,
        };
        picker.FileTypeFilter.Add(".pdf");

        nint hwnd = WindowNative.GetWindowHandle(this);
        InitializeWithWindow.Initialize(picker, hwnd);

        StorageFile? file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private async void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _zoomDebounceTimer.Stop();
        _zoomDebounceTimer.Tick -= OnZoomDebounceTimerTick;

        PagesListView.RemoveHandler(UIElement.PointerPressedEvent, _panPointerPressedHandler);
        PagesListView.RemoveHandler(UIElement.PointerMovedEvent, _panPointerMovedHandler);
        PagesListView.RemoveHandler(UIElement.PointerReleasedEvent, _panPointerReleasedHandler);
        PagesListView.RemoveHandler(UIElement.PointerCanceledEvent, _panPointerCanceledHandler);

        ViewModel.DocumentLoaded -= OnDocumentLoaded;
        ViewModel.ReadingThemeChanged -= OnReadingThemeChanged;
        ViewModel.UiThemeChanged -= OnUiThemeChanged;
        ViewModel.ZoomChanged -= OnViewModelZoomChanged;
        ViewModel.ScrollToPageRequested -= OnScrollToPageRequested;
        ViewModel.FocusSearchBoxRequested -= OnFocusSearchBoxRequested;

        if (_scrollViewer is not null)
        {
            _scrollViewer.ViewChanged -= OnScrollViewerViewChanged;
        }

        if (_xamlRootSubscribed && RootGrid.XamlRoot is not null)
        {
            RootGrid.XamlRoot.Changed -= OnXamlRootChanged;
        }

        _viewportCoordinator.Dispose();
        await ViewModel.DisposeAsync();
    }

    private static bool IsInsideScrollBar(DependencyObject? element)
    {
        DependencyObject? current = element;
        while (current is not null)
        {
            if (current is ScrollBar)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            T? nested = FindDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
