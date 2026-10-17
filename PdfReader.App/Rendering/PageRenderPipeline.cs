using System;
using System.Threading;
using System.Threading.Tasks;
using PdfReader.Core;

namespace PdfReader.App.Rendering;

public sealed class PageRenderPipeline : IDisposable
{
    private const long DefaultMaxCacheBytes = 128L * 1024 * 1024;

    private readonly record struct PageCacheKey(int PageIndex, int Width, int Height);

    private readonly IPdfDocumentService _documentService;
    private readonly LruCache<PageCacheKey, PageFrame> _cache;
    private bool _disposed;

    public PageRenderPipeline(IPdfDocumentService documentService, long maxCacheBytes = DefaultMaxCacheBytes)
    {
        ArgumentNullException.ThrowIfNull(documentService);

        _documentService = documentService;
        _cache = new LruCache<PageCacheKey, PageFrame>(maxCacheBytes);
    }

    public long CachedBytes => _cache.CurrentBytes;

    public async Task<PageFrame> RenderPageAsync(
        int pageIndex,
        PdfPageSize pageSize,
        double scale,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        PixelSize targetSize = PixelSize.FromPageSize(pageSize, scale);
        var key = new PageCacheKey(pageIndex, targetSize.Width, targetSize.Height);

        if (_cache.TryGetValue(key, out PageFrame? cachedFrame) && !cachedFrame.IsDisposed)
        {
            return cachedFrame;
        }

        RenderedPageBuffer rendered = await _documentService
            .RenderPageAsync(pageIndex, targetSize, cancellationToken)
            .ConfigureAwait(false);

        var frame = new PageFrame(rendered.PageIndex, rendered.Size, rendered.Stride, rendered.BgraPixels);
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
