using System;
using System.Threading;
using System.Threading.Tasks;
using PdfReader.Core;

namespace PdfReader.App.Rendering;

public sealed class PageRenderPipeline : IDisposable
{
    private const long DefaultMaxCacheBytes = 128L * 1024 * 1024;

    private readonly record struct PageCacheKey(
        int PageIndex,
        int Width,
        int Height,
        ReadingThemePalette Theme);

    private readonly object _themeGate = new();
    private readonly IPdfDocumentService _documentService;
    private readonly LruCache<PageCacheKey, PageFrame> _cache;
    private ReadingThemePalette _readingTheme = ReadingThemePalette.DefaultDark;
    private bool _disposed;

    public PageRenderPipeline(IPdfDocumentService documentService, long maxCacheBytes = DefaultMaxCacheBytes)
    {
        ArgumentNullException.ThrowIfNull(documentService);

        _documentService = documentService;
        _cache = new LruCache<PageCacheKey, PageFrame>(maxCacheBytes);
    }

    public long CachedBytes => _cache.CurrentBytes;

    public ReadingThemePalette ReadingTheme
    {
        get
        {
            lock (_themeGate)
            {
                return _readingTheme;
            }
        }
    }

    public bool SetReadingTheme(ReadingThemePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_themeGate)
        {
            if (_readingTheme == palette)
            {
                return false;
            }

            _readingTheme = palette;
        }

        _cache.Clear();
        return true;
    }

    public async Task<PageFrame> RenderPageAsync(
        int pageIndex,
        PdfPageSize pageSize,
        double scale,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        ReadingThemePalette activeTheme = ReadingTheme;
        PixelSize targetSize = PixelSize.FromPageSize(pageSize, scale);
        var key = new PageCacheKey(pageIndex, targetSize.Width, targetSize.Height, activeTheme);

        if (_cache.TryGetValue(key, out PageFrame? cachedFrame) && !cachedFrame.IsDisposed)
        {
            return cachedFrame;
        }

        RenderedPageBuffer rendered = await _documentService
            .RenderPageAsync(pageIndex, targetSize, cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        byte[] themedPixels = PageThemeProcessor.ApplyTheme(rendered, activeTheme);
        var frame = new PageFrame(rendered.PageIndex, rendered.Size, rendered.Stride, themedPixels);
        _cache.Set(key, frame, frame.ByteSize);
        return frame;
    }

    public void ClearCache()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _cache.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cache.Dispose();
    }
}
