using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.Resources;
using PdfReader.App.Rendering;
using PdfReader.Core;

namespace PdfReader.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IPdfDocumentService _documentService;
    private readonly PageRenderPipeline _renderPipeline;
    private readonly Func<Task<string?>> _pickPdfFileAsync;
    private readonly ResourceLoader? _resourceLoader;
    private CancellationTokenSource? _openCts;
    private bool _disposed;

    public MainViewModel(
        IPdfDocumentService documentService,
        PageRenderPipeline renderPipeline,
        Func<Task<string?>> pickPdfFileAsync)
    {
        ArgumentNullException.ThrowIfNull(documentService);
        ArgumentNullException.ThrowIfNull(renderPipeline);
        ArgumentNullException.ThrowIfNull(pickPdfFileAsync);

        _documentService = documentService;
        _renderPipeline = renderPipeline;
        _pickPdfFileAsync = pickPdfFileAsync;
        _resourceLoader = TryCreateResourceLoader();

        WindowTitle = GetString("AppDisplayName", "PDF Reader");
        StatusMessage = GetString("StatusReady", "Ready");
    }

    public event EventHandler? DocumentLoaded;

    public ObservableCollection<PdfPageItem> Pages { get; } = [];

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

    public Visibility EmptyStateVisibility => HasDocument ? Visibility.Collapsed : Visibility.Visible;

    public Visibility DocumentVisibility => HasDocument ? Visibility.Visible : Visibility.Collapsed;

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
        string fileName = Path.GetFileName(filePath);

        IsBusy = true;
        IsErrorOpen = false;
        ErrorMessage = null;
        StatusMessage = string.Format(
            CultureInfo.CurrentCulture,
            GetString("StatusOpeningFormat", "Opening {0}..."),
            fileName);

        try
        {
            _renderPipeline.ClearCache();

            PdfDocumentMetadata metadata = await _documentService.OpenDocumentAsync(filePath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            Pages.Clear();
            for (int i = 0; i < metadata.PageCount; i++)
            {
                Pages.Add(new PdfPageItem(i, metadata.PageSizes[i], Zoom));
            }

            HasDocument = true;
            WindowTitle = $"{fileName} — {GetString("AppDisplayName", "PDF Reader")}";
            StatusMessage = string.Format(
                CultureInfo.CurrentCulture,
                GetString("StatusLoadedFormat", "{0} ({1} pages)"),
                fileName,
                metadata.PageCount);

            DocumentLoaded?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer open request.
        }
        catch (Exception ex)
        {
            Pages.Clear();
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

        if (_openCts is not null)
        {
            await _openCts.CancelAsync();
            _openCts.Dispose();
            _openCts = null;
        }

        _renderPipeline.Dispose();
        await _documentService.DisposeAsync();
    }

    partial void OnHasDocumentChanged(bool value)
    {
        OnPropertyChanged(nameof(EmptyStateVisibility));
        OnPropertyChanged(nameof(DocumentVisibility));
    }

    partial void OnZoomChanged(double value)
    {
        foreach (PdfPageItem page in Pages)
        {
            page.Zoom = value;
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
