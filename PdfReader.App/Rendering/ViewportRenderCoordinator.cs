using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using PdfReader.App.ViewModels;

namespace PdfReader.App.Rendering;

public sealed class ViewportRenderCoordinator : IDisposable
{
    private const double ScaleTolerance = 0.001;
    private const int DetachImageDistancePages = 8;

    private readonly PageRenderPipeline _pipeline;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly int _bufferPages;
    private readonly Dictionary<int, CancellationTokenSource> _activeRenders = new();
    private double _rasterizationScale = 1.0;
    private bool _disposed;

    public ViewportRenderCoordinator(
        PageRenderPipeline pipeline,
        DispatcherQueue dispatcherQueue,
        int bufferPages = 2)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(dispatcherQueue);

        if (bufferPages < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferPages));
        }

        _pipeline = pipeline;
        _dispatcherQueue = dispatcherQueue;
        _bufferPages = bufferPages;
    }

    public double RasterizationScale => _rasterizationScale;

    public void SetRasterizationScale(double rasterizationScale)
    {
        if (rasterizationScale <= 0 || !double.IsFinite(rasterizationScale))
        {
            return;
        }

        _rasterizationScale = rasterizationScale;
    }

    public void UpdateViewport(
        IReadOnlyList<PdfPageItem> pages,
        double verticalOffset,
        double viewportHeight,
        double itemSpacing = 16.0)
    {
        ArgumentNullException.ThrowIfNull(pages);

        if (_disposed || pages.Count == 0 || viewportHeight <= 0)
        {
            CancelAll();
            return;
        }

        (int firstVisible, int lastVisible) = ComputeVisibleRange(pages, verticalOffset, viewportHeight, itemSpacing);
        int startIndex = Math.Max(0, firstVisible - _bufferPages);
        int endIndex = Math.Min(pages.Count - 1, lastVisible + _bufferPages);

        CancelOutOfRange(startIndex, endIndex);
        ReleaseDistantPageImages(pages, startIndex, endIndex);

        for (int i = startIndex; i <= endIndex; i++)
        {
            PdfPageItem page = pages[i];
            double targetScale = _rasterizationScale * page.Zoom;
            EnsurePageRendered(page, targetScale);
        }
    }

    public void CancelAll()
    {
        foreach (CancellationTokenSource cts in _activeRenders.Values)
        {
            cts.Cancel();
        }

        _activeRenders.Clear();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelAll();
    }

    private static (int FirstVisible, int LastVisible) ComputeVisibleRange(
        IReadOnlyList<PdfPageItem> pages,
        double verticalOffset,
        double viewportHeight,
        double itemSpacing)
    {
        double viewportTop = Math.Max(0, verticalOffset);
        double viewportBottom = viewportTop + Math.Max(1, viewportHeight);

        int firstVisible = -1;
        int lastVisible = -1;
        double currentTop = 0;

        for (int i = 0; i < pages.Count; i++)
        {
            double pageHeight = pages[i].DisplayHeight;
            double currentBottom = currentTop + pageHeight;

            if (currentBottom >= viewportTop && firstVisible == -1)
            {
                firstVisible = i;
            }

            if (currentTop <= viewportBottom)
            {
                lastVisible = i;
            }
            else if (firstVisible != -1)
            {
                break;
            }

            currentTop = currentBottom + itemSpacing;
        }

        if (firstVisible == -1)
        {
            firstVisible = Math.Max(0, pages.Count - 1);
        }

        if (lastVisible < firstVisible)
        {
            lastVisible = firstVisible;
        }

        return (firstVisible, lastVisible);
    }

    private void CancelOutOfRange(int startIndex, int endIndex)
    {
        var keysToRemove = new List<int>();
        foreach ((int pageIndex, CancellationTokenSource cts) in _activeRenders)
        {
            if (pageIndex < startIndex || pageIndex > endIndex)
            {
                cts.Cancel();
                keysToRemove.Add(pageIndex);
            }
        }

        foreach (int pageIndex in keysToRemove)
        {
            _activeRenders.Remove(pageIndex);
        }
    }

    private static void ReleaseDistantPageImages(
        IReadOnlyList<PdfPageItem> pages,
        int startIndex,
        int endIndex)
    {
        int retainStart = Math.Max(0, startIndex - DetachImageDistancePages);
        int retainEnd = Math.Min(pages.Count - 1, endIndex + DetachImageDistancePages);

        for (int i = 0; i < retainStart; i++)
        {
            if (pages[i].RenderedImage is not null)
            {
                pages[i].RenderedImage = null;
                pages[i].RenderedScale = 0;
            }
        }

        for (int i = retainEnd + 1; i < pages.Count; i++)
        {
            if (pages[i].RenderedImage is not null)
            {
                pages[i].RenderedImage = null;
                pages[i].RenderedScale = 0;
            }
        }
    }

    private void EnsurePageRendered(PdfPageItem page, double targetScale)
    {
        if (page.RenderedImage is not null && Math.Abs(page.RenderedScale - targetScale) <= ScaleTolerance)
        {
            return;
        }

        if (_activeRenders.ContainsKey(page.PageIndex))
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _activeRenders[page.PageIndex] = cts;
        _ = RenderPageCoreAsync(page, targetScale, cts);
    }

    private async Task RenderPageCoreAsync(
        PdfPageItem page,
        double targetScale,
        CancellationTokenSource cts)
    {
        CancellationToken token = cts.Token;
        page.IsRendering = true;
        try
        {
            PageFrame frame = await Task
                .Run(() => _pipeline.RenderPageAsync(page.PageIndex, page.PageSize, targetScale, token), token)
                .ConfigureAwait(false);

            if (token.IsCancellationRequested || _disposed)
            {
                return;
            }

            _dispatcherQueue.TryEnqueue(() =>
            {
                if (token.IsCancellationRequested || _disposed || frame.IsDisposed)
                {
                    return;
                }

                ImageSource imageSource = frame.CreateImageSource();
                page.RenderedImage = imageSource;
                page.RenderedScale = targetScale;
            });
        }
        catch (OperationCanceledException)
        {
            // Expected when a page scrolls out of the active viewport window.
        }
        finally
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                page.IsRendering = false;
                if (_activeRenders.TryGetValue(page.PageIndex, out CancellationTokenSource? existing) &&
                    ReferenceEquals(existing, cts))
                {
                    _activeRenders.Remove(page.PageIndex);
                }
            });

            cts.Dispose();
        }
    }
}
