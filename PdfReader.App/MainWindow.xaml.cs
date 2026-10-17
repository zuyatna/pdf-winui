using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PdfReader.App.Rendering;
using PdfReader.App.ViewModels;
using PdfReader.Core;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace PdfReader.App;

public sealed partial class MainWindow : Window
{
    private const double PageItemBottomMargin = 16.0;

    private readonly ViewportRenderCoordinator _viewportCoordinator;
    private ScrollViewer? _scrollViewer;
    private bool _xamlRootSubscribed;

    public MainWindow()
    {
        var documentService = new PdfDocumentService();
        var renderPipeline = new PageRenderPipeline(documentService);
        ViewModel = new MainViewModel(documentService, renderPipeline, PickPdfFileAsync);
        _viewportCoordinator = new ViewportRenderCoordinator(renderPipeline, DispatcherQueue);

        InitializeComponent();

        ViewModel.DocumentLoaded += OnDocumentLoaded;
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
        RefreshViewport();
    }

    private void OnPagesListViewLoaded(object sender, RoutedEventArgs e)
    {
        EnsureScrollViewerHooked();
        RefreshViewport();
    }

    private void OnPagesListViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        EnsureScrollViewerHooked();
        RefreshViewport();
    }

    private void OnDocumentLoaded(object? sender, EventArgs e)
    {
        EnsureScrollViewerHooked();
        _scrollViewer?.ChangeView(null, 0, null, disableAnimation: true);
        DispatcherQueue.TryEnqueue(RefreshViewport);
    }

    private void OnScrollViewerViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        RefreshViewport();
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

    private void RefreshViewport()
    {
        if (RootGrid.XamlRoot is not null)
        {
            _viewportCoordinator.SetRasterizationScale(RootGrid.XamlRoot.RasterizationScale);
        }

        double verticalOffset = _scrollViewer?.VerticalOffset ?? 0.0;
        double viewportHeight = _scrollViewer?.ViewportHeight > 0
            ? _scrollViewer.ViewportHeight
            : PagesListView.ActualHeight;

        _viewportCoordinator.UpdateViewport(
            ViewModel.Pages,
            verticalOffset,
            viewportHeight,
            PageItemBottomMargin);
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
        ViewModel.DocumentLoaded -= OnDocumentLoaded;
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
