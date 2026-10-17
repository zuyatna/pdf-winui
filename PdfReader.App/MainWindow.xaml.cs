using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PdfReader.App.Rendering;
using PdfReader.App.ViewModels;
using PdfReader.Core;
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
    private ScrollViewer? _scrollViewer;
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

        InitializeComponent();

        ViewModel.DocumentLoaded += OnDocumentLoaded;
        ViewModel.ReadingThemeChanged += OnReadingThemeChanged;
        ViewModel.UiThemeChanged += OnUiThemeChanged;
        ViewModel.ZoomChanged += OnViewModelZoomChanged;
        ViewModel.ScrollToPageRequested += OnScrollToPageRequested;
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

    private void OnScrollViewerViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        RefreshViewport(triggerRenders: !_isZoomSettling);
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

        ViewModel.DocumentLoaded -= OnDocumentLoaded;
        ViewModel.ReadingThemeChanged -= OnReadingThemeChanged;
        ViewModel.UiThemeChanged -= OnUiThemeChanged;
        ViewModel.ZoomChanged -= OnViewModelZoomChanged;
        ViewModel.ScrollToPageRequested -= OnScrollToPageRequested;

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
