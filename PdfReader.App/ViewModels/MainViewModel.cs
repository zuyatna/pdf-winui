using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;
using PdfReader.App.Rendering;
using PdfReader.App.Services;
using PdfReader.Core;
using Windows.ApplicationModel.DataTransfer;

namespace PdfReader.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IPdfDocumentService _documentService;
    private readonly PageRenderPipeline _renderPipeline;
    private readonly ReaderSettingsService _settingsService;
    private readonly Func<Task<string?>> _pickPdfFileAsync;
    private readonly ResourceLoader? _resourceLoader;
    private IReadOnlyList<DocumentViewState> _recentDocumentStates = [];
    private IReadOnlyList<PdfSearchMatch> _searchMatches = [];
    private int _activeSearchMatchIndex = -1;
    private int _selectedPageIndex = -1;
    private CancellationTokenSource? _openCts;
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _selectionCts;
    private string? _currentFilePath;
    private double _viewportWidthDip = 900.0;
    private double _viewportHeightDip = 700.0;
    private bool _suppressThemeSync;
    private bool _isUpdatingPageFromScroll;
    private bool _disposed;

    public MainViewModel(
        IPdfDocumentService documentService,
        PageRenderPipeline renderPipeline,
        Func<Task<string?>> pickPdfFileAsync,
        ReaderSettingsService? settingsService = null)
    {
        ArgumentNullException.ThrowIfNull(documentService);
        ArgumentNullException.ThrowIfNull(renderPipeline);
        ArgumentNullException.ThrowIfNull(pickPdfFileAsync);

        _documentService = documentService;
        _renderPipeline = renderPipeline;
        _pickPdfFileAsync = pickPdfFileAsync;
        _settingsService = settingsService ?? new ReaderSettingsService();
        _resourceLoader = TryCreateResourceLoader();

        WindowTitle = GetString("AppDisplayName", "PDF Reader");
        StatusMessage = GetString("StatusReady", "Ready");
        SearchStatusText = GetString("SearchNoMatches", "0 / 0");

        ReaderSettings initialSettings = _settingsService.Load();
        ApplyLoadedSettings(initialSettings);
    }

    public event EventHandler? DocumentLoaded;

    public event EventHandler? UiThemeChanged;

    public event EventHandler? ReadingThemeChanged;

    public event EventHandler? ZoomChanged;

    public event EventHandler<int>? ScrollToPageRequested;

    public event EventHandler? FocusSearchBoxRequested;

    public ObservableCollection<PdfPageItem> Pages { get; } = [];

    public ObservableCollection<RecentFileItem> RecentFiles { get; } = [];

    public PageRenderPipeline RenderPipeline => _renderPipeline;

    [ObservableProperty]
    public partial string WindowTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial bool IsErrorOpen { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool HasDocument { get; set; }

    [ObservableProperty]
    public partial double Zoom { get; set; } = 1.0;

    [ObservableProperty]
    public partial ZoomMode ActiveZoomMode { get; set; } = ZoomMode.Custom;

    [ObservableProperty]
    public partial int CurrentPageNumber { get; set; } = 1;

    [ObservableProperty]
    public partial int TotalPages { get; set; }

    [ObservableProperty]
    public partial string GoToPageInput { get; set; } = "1";

    [ObservableProperty]
    public partial string SelectedText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasTextSelection { get; set; }

    [ObservableProperty]
    public partial bool IsTextSelectionMode { get; set; }

    [ObservableProperty]
    public partial bool IsSearchBarOpen { get; set; }

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool SearchMatchCase { get; set; }

    [ObservableProperty]
    public partial string SearchStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasSearchMatches { get; set; }

    [ObservableProperty]
    public partial bool IsSettingsPaneOpen { get; set; }

    [ObservableProperty]
    public partial int SelectedUiThemeIndex { get; set; }

    [ObservableProperty]
    public partial int SelectedReadingThemeIndex { get; set; }

    [ObservableProperty]
    public partial bool IsDarkReadingMode { get; set; }

    [ObservableProperty]
    public partial string DarkPageBackgroundHex { get; set; } = ReaderSettings.DefaultDarkPageBackgroundHex;

    [ObservableProperty]
    public partial string DarkTextHex { get; set; } = ReaderSettings.DefaultDarkTextHex;

    [ObservableProperty]
    public partial string DarkCanvasHex { get; set; } = ReaderSettings.DefaultDarkCanvasHex;

    [ObservableProperty]
    public partial double ImageDimPercentage { get; set; } = ReaderSettings.DefaultImageDimPercentage;

    [ObservableProperty]
    public partial Brush? CanvasBackgroundBrush { get; set; }

    [ObservableProperty]
    public partial Brush? PageCardBackgroundBrush { get; set; }

    [ObservableProperty]
    public partial Brush? SwatchPageBackgroundBrush { get; set; }

    [ObservableProperty]
    public partial Brush? SwatchTextBrush { get; set; }

    [ObservableProperty]
    public partial Brush? SwatchCanvasBrush { get; set; }

    public ElementTheme RootRequestedTheme => (AppUiTheme)SelectedUiThemeIndex switch
    {
        AppUiTheme.Light => ElementTheme.Light,
        AppUiTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    public Visibility EmptyStateVisibility => HasDocument ? Visibility.Collapsed : Visibility.Visible;

    public Visibility DocumentVisibility => HasDocument ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SearchBarVisibility => HasDocument && IsSearchBarOpen ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RecentFilesVisibility => !HasDocument && RecentFiles.Count > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string ZoomPercentageText => $"{Math.Round(Zoom * 100.0):0}%";

    public string TotalPagesSuffixText => HasDocument ? $"/ {TotalPages}" : "/ 0";

    public bool IsFitWidthActive => ActiveZoomMode == ZoomMode.FitWidth;

    public bool IsFitPageActive => ActiveZoomMode == ZoomMode.FitPage;

    public bool IsReadingInteractionMode => !IsTextSelectionMode;

    public string DragDropOpenCaption => GetString("DragDropOpenCaption", "Open PDF");

    [RelayCommand]
    public void SetReadingInteractionMode()
    {
        IsTextSelectionMode = false;
    }

    [RelayCommand]
    public void SetTextSelectionMode()
    {
        IsTextSelectionMode = true;
    }

    [RelayCommand]
    public void ToggleSettingsPane()
    {
        IsSettingsPaneOpen = !IsSettingsPaneOpen;
    }

    [RelayCommand]
    public void ToggleDarkReadingMode()
    {
        SelectedReadingThemeIndex = SelectedReadingThemeIndex == (int)ReadingThemeMode.Dark
            ? (int)ReadingThemeMode.Original
            : (int)ReadingThemeMode.Dark;
    }

    [RelayCommand]
    public void ToggleSearchBar()
    {
        if (!HasDocument)
        {
            return;
        }

        IsSearchBarOpen = !IsSearchBarOpen;
        if (IsSearchBarOpen)
        {
            FocusSearchBoxRequested?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            ClearSearchHighlights();
        }
    }

    [RelayCommand]
    public void OpenSearchBar()
    {
        if (!HasDocument)
        {
            return;
        }

        IsSearchBarOpen = true;
        FocusSearchBoxRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void CloseSearchBar()
    {
        IsSearchBarOpen = false;
        ClearSearchHighlights();
    }

    [RelayCommand]
    public async Task ExecuteSearchAsync()
    {
        if (_disposed || !HasDocument)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            ClearSearchHighlights();
            return;
        }

        var newCts = new CancellationTokenSource();
        CancellationTokenSource? prevCts = Interlocked.Exchange(ref _searchCts, newCts);
        if (prevCts is not null)
        {
            await prevCts.CancelAsync();
            prevCts.Dispose();
        }

        CancellationToken cancellationToken = newCts.Token;

        try
        {
            IReadOnlyList<PdfSearchMatch> matches = await _documentService.SearchDocumentAsync(
                SearchQuery,
                SearchMatchCase,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            ApplySearchMatches(matches);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer search request.
        }
    }

    [RelayCommand]
    public void NextSearchMatch()
    {
        if (_searchMatches.Count == 0)
        {
            return;
        }

        int nextIndex = (_activeSearchMatchIndex + 1) % _searchMatches.Count;
        FocusSearchMatchAt(nextIndex);
    }

    [RelayCommand]
    public void PreviousSearchMatch()
    {
        if (_searchMatches.Count == 0)
        {
            return;
        }

        int prevIndex = (_activeSearchMatchIndex - 1 + _searchMatches.Count) % _searchMatches.Count;
        FocusSearchMatchAt(prevIndex);
    }

    [RelayCommand]
    public void CopySelectedText()
    {
        if (!HasTextSelection || string.IsNullOrEmpty(SelectedText))
        {
            return;
        }

        var dataPackage = new DataPackage();
        dataPackage.SetText(SelectedText);
        Clipboard.SetContent(dataPackage);
        Clipboard.Flush();

        StatusMessage = GetString("StatusTextCopied", "Copied selected text to clipboard");
    }

    public async Task RequestPageTextSelectionAsync(
        int pageIndex,
        double startNormalizedX,
        double startNormalizedY,
        double endNormalizedX,
        double endNormalizedY)
    {
        if (_disposed || !HasDocument || pageIndex < 0 || pageIndex >= Pages.Count)
        {
            return;
        }

        var newCts = new CancellationTokenSource();
        CancellationTokenSource? prevCts = Interlocked.Exchange(ref _selectionCts, newCts);
        if (prevCts is not null)
        {
            await prevCts.CancelAsync();
            prevCts.Dispose();
        }

        CancellationToken cancellationToken = newCts.Token;

        try
        {
            PdfTextSelection? selection = await _documentService.SelectTextAsync(
                pageIndex,
                startNormalizedX,
                startNormalizedY,
                endNormalizedX,
                endNormalizedY,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            ApplyTextSelection(selection);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer pointer drag position.
        }
    }

    public void ClearTextSelection()
    {
        if (_selectedPageIndex >= 0 && _selectedPageIndex < Pages.Count)
        {
            Pages[_selectedPageIndex].SelectionRects = [];
        }

        _selectedPageIndex = -1;
        SelectedText = string.Empty;
        HasTextSelection = false;
    }

    [RelayCommand]
    public void ZoomIn()
    {
        ActiveZoomMode = ZoomMode.Custom;
        Zoom = ZoomCalculator.StepZoom(Zoom, steps: 1);
        PersistCurrentDocumentState();
    }

    [RelayCommand]
    public void ZoomOut()
    {
        ActiveZoomMode = ZoomMode.Custom;
        Zoom = ZoomCalculator.StepZoom(Zoom, steps: -1);
        PersistCurrentDocumentState();
    }

    [RelayCommand]
    public void ResetZoom()
    {
        ActiveZoomMode = ZoomMode.Custom;
        Zoom = 1.0;
        PersistCurrentDocumentState();
    }

    [RelayCommand]
    public void SetFitWidth()
    {
        ActiveZoomMode = ZoomMode.FitWidth;
        RecalculateFitZoom();
        PersistCurrentDocumentState();
    }

    [RelayCommand]
    public void SetFitPage()
    {
        ActiveZoomMode = ZoomMode.FitPage;
        RecalculateFitZoom();
        PersistCurrentDocumentState();
    }

    public void StepZoomFromWheel(int wheelDelta)
    {
        if (!HasDocument || wheelDelta == 0)
        {
            return;
        }

        int steps = wheelDelta > 0 ? 1 : -1;
        ActiveZoomMode = ZoomMode.Custom;
        Zoom = ZoomCalculator.StepZoom(Zoom, steps);
        PersistCurrentDocumentState();
    }

    public void UpdateViewportDimensions(double viewportWidthDip, double viewportHeightDip)
    {
        if (viewportWidthDip > 0 && double.IsFinite(viewportWidthDip))
        {
            _viewportWidthDip = viewportWidthDip;
        }

        if (viewportHeightDip > 0 && double.IsFinite(viewportHeightDip))
        {
            _viewportHeightDip = viewportHeightDip;
        }

        if (ActiveZoomMode != ZoomMode.Custom)
        {
            RecalculateFitZoom();
        }
    }

    [RelayCommand]
    public void PreviousPage()
    {
        if (!HasDocument || CurrentPageNumber <= 1)
        {
            return;
        }

        NavigateToPageNumber(CurrentPageNumber - 1);
    }

    [RelayCommand]
    public void NextPage()
    {
        if (!HasDocument || CurrentPageNumber >= TotalPages)
        {
            return;
        }

        NavigateToPageNumber(CurrentPageNumber + 1);
    }

    [RelayCommand]
    public void GoToPage()
    {
        if (!HasDocument || TotalPages <= 0)
        {
            return;
        }

        if (!int.TryParse(GoToPageInput?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int targetPage))
        {
            GoToPageInput = CurrentPageNumber.ToString(CultureInfo.InvariantCulture);
            return;
        }

        NavigateToPageNumber(targetPage);
    }

    public void UpdateVisiblePageFromScroll(int zeroBasedPageIndex)
    {
        if (!HasDocument || Pages.Count == 0)
        {
            return;
        }

        int pageNumber = Math.Clamp(zeroBasedPageIndex + 1, 1, Pages.Count);
        if (CurrentPageNumber == pageNumber)
        {
            return;
        }

        _isUpdatingPageFromScroll = true;
        try
        {
            CurrentPageNumber = pageNumber;
            GoToPageInput = pageNumber.ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            _isUpdatingPageFromScroll = false;
        }

        PersistCurrentDocumentState();
    }

    [RelayCommand]
    public void ApplyPalette()
    {
        if (!RgbColor.TryParseHex(DarkPageBackgroundHex, out RgbColor pageBg) ||
            !RgbColor.TryParseHex(DarkTextHex, out RgbColor text) ||
            !RgbColor.TryParseHex(DarkCanvasHex, out RgbColor canvas))
        {
            ErrorMessage = GetString(
                "StatusInvalidColorHex",
                "Invalid color hex format. Please use #RRGGBB (for example #262624).");
            IsErrorOpen = true;
            return;
        }

        IsErrorOpen = false;
        ErrorMessage = null;
        DarkPageBackgroundHex = pageBg.ToHex();
        DarkTextHex = text.ToHex();
        DarkCanvasHex = canvas.ToHex();

        PersistAndApplyReadingTheme();
    }

    [RelayCommand]
    public void ResetPaletteDefaults()
    {
        _suppressThemeSync = true;
        try
        {
            DarkPageBackgroundHex = ReaderSettings.DefaultDarkPageBackgroundHex;
            DarkTextHex = ReaderSettings.DefaultDarkTextHex;
            DarkCanvasHex = ReaderSettings.DefaultDarkCanvasHex;
            ImageDimPercentage = ReaderSettings.DefaultImageDimPercentage;
        }
        finally
        {
            _suppressThemeSync = false;
        }

        IsErrorOpen = false;
        ErrorMessage = null;
        PersistAndApplyReadingTheme();
    }

    [RelayCommand]
    public async Task OpenDocumentAsync()
    {
        if (_disposed)
        {
            return;
        }

        string? selectedPath = await _pickPdfFileAsync();
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        await OpenFileAsync(selectedPath);
    }

    [RelayCommand]
    public async Task OpenRecentFileAsync(RecentFileItem? item)
    {
        if (_disposed || item is null || string.IsNullOrWhiteSpace(item.FilePath))
        {
            return;
        }

        await OpenFileAsync(item.FilePath);
    }

    [RelayCommand]
    public void ClearRecentFiles()
    {
        _recentDocumentStates = [];
        RefreshRecentFilesCollection();
        SaveCurrentSettings();
    }

    public async Task OpenFileAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var newCts = new CancellationTokenSource();
        CancellationTokenSource? previousCts = Interlocked.Exchange(ref _openCts, newCts);
        if (previousCts is not null)
        {
            await previousCts.CancelAsync();
            previousCts.Dispose();
        }

        CancellationToken cancellationToken = newCts.Token;
        string fullPath = Path.GetFullPath(filePath);
        string fileName = Path.GetFileName(fullPath);

        IsBusy = true;
        IsErrorOpen = false;
        ErrorMessage = null;
        ClearTextSelection();
        ClearSearchHighlights();
        StatusMessage = string.Format(
            CultureInfo.CurrentCulture,
            GetString("StatusOpeningFormat", "Opening {0}..."),
            fileName);

        try
        {
            _renderPipeline.ClearCache();

            PdfDocumentMetadata metadata = await _documentService.OpenDocumentAsync(fullPath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            ReaderSettings currentSettings = BuildCurrentSettings();
            DocumentViewState? savedState = currentSettings.FindDocumentState(fullPath);

            ZoomMode restoredZoomMode = savedState?.ZoomMode ?? ZoomMode.Custom;
            double restoredZoom = savedState is not null
                ? ZoomCalculator.Clamp(savedState.Zoom)
                : 1.0;

            if (restoredZoomMode == ZoomMode.FitWidth && metadata.PageSizes.Count > 0)
            {
                restoredZoom = ZoomCalculator.CalculateFitWidthZoom(metadata.PageSizes[0], _viewportWidthDip);
            }
            else if (restoredZoomMode == ZoomMode.FitPage && metadata.PageSizes.Count > 0)
            {
                restoredZoom = ZoomCalculator.CalculateFitPageZoom(
                    metadata.PageSizes[0],
                    _viewportWidthDip,
                    _viewportHeightDip);
            }

            ActiveZoomMode = restoredZoomMode;
            Zoom = restoredZoom;

            Pages.Clear();
            for (int i = 0; i < metadata.PageCount; i++)
            {
                Pages.Add(new PdfPageItem(
                    i,
                    metadata.PageSizes[i],
                    restoredZoom,
                    PageCardBackgroundBrush,
                    IsTextSelectionMode,
                    RequestPageTextSelectionAsync,
                    ClearTextSelection));
            }

            _currentFilePath = fullPath;
            TotalPages = metadata.PageCount;
            int restoredPage = Math.Clamp(savedState?.LastPageNumber ?? 1, 1, metadata.PageCount);
            CurrentPageNumber = restoredPage;
            GoToPageInput = restoredPage.ToString(CultureInfo.InvariantCulture);

            HasDocument = true;
            WindowTitle = $"{fileName} — {GetString("AppDisplayName", "PDF Reader")}";
            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                GetString("StatusLoadedFormat", "{0} ({1} pages)"),
                fileName,
                metadata.PageCount);

            PersistCurrentDocumentState();
            DocumentLoaded?.Invoke(this, EventArgs.Empty);

            if (restoredPage > 1)
            {
                ScrollToPageRequested?.Invoke(this, restoredPage - 1);
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer open request.
        }
        catch (Exception ex)
        {
            _currentFilePath = null;
            Pages.Clear();
            TotalPages = 0;
            CurrentPageNumber = 1;
            GoToPageInput = "1";
            HasDocument = false;
            WindowTitle = GetString("AppDisplayName", "PDF Reader");
            ErrorMessage = string.Format(
                CultureInfo.CurrentCulture,
                GetString("StatusOpenFailedFormat", "Could not open PDF: {0}"),
                ex.Message);
            IsErrorOpen = true;
            StatusMessage = GetString("StatusReady", "Ready");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await CancelAndDisposeAsync(Interlocked.Exchange(ref _openCts, null));
        await CancelAndDisposeAsync(Interlocked.Exchange(ref _searchCts, null));
        await CancelAndDisposeAsync(Interlocked.Exchange(ref _selectionCts, null));

        _renderPipeline.Dispose();
        await _documentService.DisposeAsync();
    }

    partial void OnHasDocumentChanged(bool value)
    {
        OnPropertyChanged(nameof(EmptyStateVisibility));
        OnPropertyChanged(nameof(DocumentVisibility));
        OnPropertyChanged(nameof(SearchBarVisibility));
        OnPropertyChanged(nameof(RecentFilesVisibility));
        OnPropertyChanged(nameof(TotalPagesSuffixText));
    }

    partial void OnIsTextSelectionModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsReadingInteractionMode));
        foreach (PdfPageItem page in Pages)
        {
            page.IsTextSelectionMode = value;
        }

        if (!value)
        {
            ClearTextSelection();
        }
    }

    partial void OnIsSearchBarOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(SearchBarVisibility));
    }

    partial void OnSearchMatchCaseChanged(bool value)
    {
        if (HasDocument && IsSearchBarOpen && !string.IsNullOrWhiteSpace(SearchQuery))
        {
            _ = ExecuteSearchAsync();
        }
    }

    partial void OnTotalPagesChanged(int value)
    {
        OnPropertyChanged(nameof(TotalPagesSuffixText));
    }

    partial void OnZoomChanged(double value)
    {
        OnPropertyChanged(nameof(ZoomPercentageText));
        foreach (PdfPageItem page in Pages)
        {
            page.Zoom = value;
        }

        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnActiveZoomModeChanged(ZoomMode value)
    {
        OnPropertyChanged(nameof(IsFitWidthActive));
        OnPropertyChanged(nameof(IsFitPageActive));
    }

    partial void OnSelectedUiThemeIndexChanged(int value)
    {
        if (_suppressThemeSync)
        {
            return;
        }

        OnPropertyChanged(nameof(RootRequestedTheme));
        SaveCurrentSettings();
        UiThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedReadingThemeIndexChanged(int value)
    {
        if (_suppressThemeSync)
        {
            return;
        }

        _suppressThemeSync = true;
        try
        {
            IsDarkReadingMode = value == (int)ReadingThemeMode.Dark;
        }
        finally
        {
            _suppressThemeSync = false;
        }

        PersistAndApplyReadingTheme();
    }

    partial void OnIsDarkReadingModeChanged(bool value)
    {
        if (_suppressThemeSync)
        {
            return;
        }

        SelectedReadingThemeIndex = value
            ? (int)ReadingThemeMode.Dark
            : (int)ReadingThemeMode.Original;
    }

    partial void OnImageDimPercentageChanged(double value)
    {
        if (_suppressThemeSync)
        {
            return;
        }

        PersistAndApplyReadingTheme();
    }

    private void ApplyTextSelection(PdfTextSelection? selection)
    {
        if (selection is null || string.IsNullOrWhiteSpace(selection.SelectedText))
        {
            ClearTextSelection();
            return;
        }

        if (_selectedPageIndex >= 0 && _selectedPageIndex < Pages.Count && _selectedPageIndex != selection.PageIndex)
        {
            Pages[_selectedPageIndex].SelectionRects = [];
        }

        _selectedPageIndex = selection.PageIndex;
        if (_selectedPageIndex >= 0 && _selectedPageIndex < Pages.Count)
        {
            Pages[_selectedPageIndex].SelectionRects = selection.HighlightRects;
        }

        SelectedText = selection.SelectedText;
        HasTextSelection = true;
    }

    private void ApplySearchMatches(IReadOnlyList<PdfSearchMatch> matches)
    {
        foreach (PdfPageItem page in Pages)
        {
            page.SearchHighlightRects = [];
            page.ActiveSearchHighlightRects = [];
        }

        _searchMatches = matches;
        if (matches.Count == 0)
        {
            _activeSearchMatchIndex = -1;
            HasSearchMatches = false;
            SearchStatusText = GetString("SearchNoMatches", "0 / 0");
            return;
        }

        var rectsByPage = new Dictionary<int, List<NormalizedRect>>();
        foreach (PdfSearchMatch match in matches)
        {
            if (!rectsByPage.TryGetValue(match.PageIndex, out List<NormalizedRect>? pageRects))
            {
                pageRects = [];
                rectsByPage[match.PageIndex] = pageRects;
            }

            pageRects.AddRange(match.HighlightRects);
        }

        foreach ((int pageIndex, List<NormalizedRect> rects) in rectsByPage)
        {
            if (pageIndex >= 0 && pageIndex < Pages.Count)
            {
                Pages[pageIndex].SearchHighlightRects = rects.AsReadOnly();
            }
        }

        HasSearchMatches = true;
        FocusSearchMatchAt(0);
    }

    private void FocusSearchMatchAt(int matchIndex)
    {
        if (_searchMatches.Count == 0 || matchIndex < 0 || matchIndex >= _searchMatches.Count)
        {
            return;
        }

        if (_activeSearchMatchIndex >= 0 && _activeSearchMatchIndex < _searchMatches.Count)
        {
            int oldPageIdx = _searchMatches[_activeSearchMatchIndex].PageIndex;
            if (oldPageIdx >= 0 && oldPageIdx < Pages.Count)
            {
                Pages[oldPageIdx].ActiveSearchHighlightRects = [];
            }
        }

        _activeSearchMatchIndex = matchIndex;
        PdfSearchMatch activeMatch = _searchMatches[matchIndex];
        if (activeMatch.PageIndex >= 0 && activeMatch.PageIndex < Pages.Count)
        {
            Pages[activeMatch.PageIndex].ActiveSearchHighlightRects = activeMatch.HighlightRects;
        }

        SearchStatusText = $"{matchIndex + 1} / {_searchMatches.Count}";
        NavigateToPageNumber(activeMatch.PageIndex + 1);
    }

    private void ClearSearchHighlights()
    {
        _searchMatches = [];
        _activeSearchMatchIndex = -1;
        HasSearchMatches = false;
        SearchStatusText = GetString("SearchNoMatches", "0 / 0");

        foreach (PdfPageItem page in Pages)
        {
            page.SearchHighlightRects = [];
            page.ActiveSearchHighlightRects = [];
        }
    }

    private void NavigateToPageNumber(int targetPageNumber)
    {
        if (!HasDocument || Pages.Count == 0 || _isUpdatingPageFromScroll)
        {
            return;
        }

        int clamped = Math.Clamp(targetPageNumber, 1, Pages.Count);
        CurrentPageNumber = clamped;
        GoToPageInput = clamped.ToString(CultureInfo.InvariantCulture);
        PersistCurrentDocumentState();
        ScrollToPageRequested?.Invoke(this, clamped - 1);
    }

    private void RecalculateFitZoom()
    {
        if (!HasDocument || Pages.Count == 0)
        {
            return;
        }

        int pageIdx = Math.Clamp(CurrentPageNumber - 1, 0, Pages.Count - 1);
        PdfPageSize referenceSize = Pages[pageIdx].PageSize;

        if (ActiveZoomMode == ZoomMode.FitWidth)
        {
            Zoom = ZoomCalculator.CalculateFitWidthZoom(referenceSize, _viewportWidthDip);
        }
        else if (ActiveZoomMode == ZoomMode.FitPage)
        {
            Zoom = ZoomCalculator.CalculateFitPageZoom(referenceSize, _viewportWidthDip, _viewportHeightDip);
        }
    }

    private void ApplyLoadedSettings(ReaderSettings settings)
    {
        _suppressThemeSync = true;
        try
        {
            SelectedUiThemeIndex = (int)settings.UiTheme;
            SelectedReadingThemeIndex = (int)settings.ReadingTheme;
            IsDarkReadingMode = settings.ReadingTheme == ReadingThemeMode.Dark;
            DarkPageBackgroundHex = settings.DarkPageBackgroundHex;
            DarkTextHex = settings.DarkTextHex;
            DarkCanvasHex = settings.DarkCanvasHex;
            ImageDimPercentage = Math.Clamp(settings.ImageDimPercentage, 0.0, 50.0);
            _recentDocumentStates = settings.RecentDocuments ?? [];
            RefreshRecentFilesCollection();
        }
        finally
        {
            _suppressThemeSync = false;
        }

        UpdateThemeBrushesAndPipeline(settings);
    }

    private void RefreshRecentFilesCollection()
    {
        RecentFiles.Clear();
        foreach (DocumentViewState state in _recentDocumentStates)
        {
            if (string.IsNullOrWhiteSpace(state.FilePath))
            {
                continue;
            }

            string fileName = Path.GetFileName(state.FilePath);
            string directory = Path.GetDirectoryName(state.FilePath) ?? string.Empty;
            RecentFiles.Add(new RecentFileItem(
                state.FilePath,
                fileName,
                directory,
                state.LastPageNumber,
                state.Zoom,
                state.ZoomMode));
        }

        OnPropertyChanged(nameof(RecentFilesVisibility));
    }

    private void PersistCurrentDocumentState()
    {
        if (!HasDocument || string.IsNullOrWhiteSpace(_currentFilePath))
        {
            return;
        }

        var state = new DocumentViewState(
            _currentFilePath,
            Math.Max(1, CurrentPageNumber),
            ZoomCalculator.Clamp(Zoom),
            ActiveZoomMode,
            DateTimeOffset.UtcNow);

        ReaderSettings updated = BuildCurrentSettings().WithUpdatedDocumentState(state);
        _recentDocumentStates = updated.RecentDocuments;
        RefreshRecentFilesCollection();
        _settingsService.Save(updated);
    }

    private void PersistAndApplyReadingTheme()
    {
        ReaderSettings settings = SaveCurrentSettings();
        bool changed = UpdateThemeBrushesAndPipeline(settings);
        if (changed)
        {
            ReadingThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private ReaderSettings BuildCurrentSettings()
    {
        return new ReaderSettings
        {
            UiTheme = Enum.IsDefined(typeof(AppUiTheme), SelectedUiThemeIndex)
                ? (AppUiTheme)SelectedUiThemeIndex
                : AppUiTheme.System,
            ReadingTheme = SelectedReadingThemeIndex == (int)ReadingThemeMode.Original
                ? ReadingThemeMode.Original
                : ReadingThemeMode.Dark,
            DarkPageBackgroundHex = DarkPageBackgroundHex,
            DarkTextHex = DarkTextHex,
            DarkCanvasHex = DarkCanvasHex,
            ImageDimPercentage = Math.Clamp(ImageDimPercentage, 0.0, 50.0),
            RecentDocuments = _recentDocumentStates,
        };
    }

    private ReaderSettings SaveCurrentSettings()
    {
        ReaderSettings settings = BuildCurrentSettings();
        _settingsService.Save(settings);
        return settings;
    }

    private bool UpdateThemeBrushesAndPipeline(ReaderSettings settings)
    {
        ReadingThemePalette palette = settings.ToReadingThemePalette();

        RgbColor darkBg = RgbColor.TryParseHex(settings.DarkPageBackgroundHex, out RgbColor pBg)
            ? pBg
            : RgbColor.DefaultDarkPageBackground;
        RgbColor darkText = RgbColor.TryParseHex(settings.DarkTextHex, out RgbColor pText)
            ? pText
            : RgbColor.DefaultDarkText;
        RgbColor darkCanvas = RgbColor.TryParseHex(settings.DarkCanvasHex, out RgbColor pCanvas)
            ? pCanvas
            : RgbColor.DefaultDarkCanvas;

        SwatchPageBackgroundBrush = new SolidColorBrush(PageThemeProcessor.ToUiColor(darkBg));
        SwatchTextBrush = new SolidColorBrush(PageThemeProcessor.ToUiColor(darkText));
        SwatchCanvasBrush = new SolidColorBrush(PageThemeProcessor.ToUiColor(darkCanvas));

        CanvasBackgroundBrush = palette.IsRemapEnabled
            ? new SolidColorBrush(PageThemeProcessor.ToUiColor(palette.Canvas))
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        PageCardBackgroundBrush = new SolidColorBrush(PageThemeProcessor.ToUiColor(palette.PageBackground));

        foreach (PdfPageItem page in Pages)
        {
            page.PageBackgroundBrush = PageCardBackgroundBrush;
        }

        return _renderPipeline.SetReadingTheme(palette);
    }

    private static async Task CancelAndDisposeAsync(CancellationTokenSource? cts)
    {
        if (cts is not null)
        {
            await cts.CancelAsync();
            cts.Dispose();
        }
    }

    private static ResourceLoader? TryCreateResourceLoader()
    {
        try
        {
            return new ResourceLoader();
        }
        catch
        {
            return null;
        }
    }

    private string GetString(string resourceKey, string fallback)
    {
        if (_resourceLoader is null)
        {
            return fallback;
        }

        try
        {
            string value = _resourceLoader.GetString(resourceKey);
            return string.IsNullOrEmpty(value) ? fallback : value;
        }
        catch
        {
            return fallback;
        }
    }
}
