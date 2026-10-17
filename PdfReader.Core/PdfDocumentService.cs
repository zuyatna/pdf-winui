using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using PDFiumCore;

namespace PdfReader.Core;

public sealed partial class PdfDocumentService : IPdfDocumentService
{
    private const int FpdfPageObjImage = 3;
    private const int FpdfPageObjForm = 5;
    private const int MaxFormRecursionDepth = 8;
    private const int ReferenceDeviceScale = 100000;
    private const double HitTestDirectTolerancePoints = 12.0;
    private const double HitTestFallbackTolerancePoints = 48.0;
    private const ulong FpdfMatchCaseFlag = 0x00000001UL;

    private static int s_libraryRefCount;

    private readonly PdfiumWorker _worker;
    private readonly bool _ownsWorker;
    private NativeResource<FpdfDocumentT>? _documentResource;
    private PdfDocumentMetadata? _currentDocument;
    private bool _libraryInitialized;
    private bool _disposed;

    public PdfDocumentService(PdfiumWorker? worker = null, bool ownsWorker = false)
    {
        if (worker is null)
        {
            _worker = PdfiumWorker.Shared;
            _ownsWorker = false;
        }
        else
        {
            _worker = worker;
            _ownsWorker = ownsWorker;
        }
    }

    public PdfDocumentMetadata? CurrentDocument => Volatile.Read(ref _currentDocument);

    public Task<PdfDocumentMetadata> OpenDocumentAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);

        string fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("PDF file was not found.", fullPath);
        }

        return _worker.RunAsync(
            () =>
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                EnsureLibraryInitializedOnWorker();

                CloseDocumentOnWorker();

                FpdfDocumentT? rawDoc = fpdfview.FPDF_LoadDocument(fullPath, null);
                if (rawDoc is null || rawDoc.__Instance == IntPtr.Zero)
                {
                    ulong error = fpdfview.FPDF_GetLastError();
                    throw new InvalidDataException($"Failed to load PDF document '{fullPath}' (PDFium error {error}).");
                }

                var docResource = new NativeResource<FpdfDocumentT>(rawDoc, fpdfview.FPDF_CloseDocument);
                try
                {
                    int pageCount = fpdfview.FPDF_GetPageCount(docResource.Handle);
                    if (pageCount <= 0)
                    {
                        throw new InvalidDataException($"PDF document '{fullPath}' contains no pages.");
                    }

                    var pageSizes = new PdfPageSize[pageCount];
                    for (int i = 0; i < pageCount; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        double width = 0;
                        double height = 0;
                        int status = fpdfview.FPDF_GetPageSizeByIndex(docResource.Handle, i, ref width, ref height);
                        if (status == 0 || width <= 0 || height <= 0)
                        {
                            using NativeResource<FpdfPageT> page = LoadPageOnWorker(docResource.Handle, i);
                            width = fpdfview.FPDF_GetPageWidth(page.Handle);
                            height = fpdfview.FPDF_GetPageHeight(page.Handle);
                        }

                        pageSizes[i] = new PdfPageSize(width, height);
                    }

                    var metadata = new PdfDocumentMetadata(fullPath, pageCount, Array.AsReadOnly(pageSizes));
                    _documentResource = docResource;
                    Volatile.Write(ref _currentDocument, metadata);
                    return metadata;
                }
                catch
                {
                    docResource.Dispose();
                    throw;
                }
            },
            cancellationToken);
    }

    public Task CloseDocumentAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);
        return _worker.RunAsync(CloseDocumentOnWorker, cancellationToken);
    }

    public Task<RenderedPageBuffer> RenderPageAsync(
        int pageIndex,
        PixelSize targetSize,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);

        if (targetSize.Width <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetSize), "Target pixel width must be positive.");

        if (targetSize.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetSize), "Target pixel height must be positive.");

        return _worker.RunAsync(
            () =>
            {
                EnsureOpenPageIndexOnWorker(pageIndex);
                cancellationToken.ThrowIfCancellationRequested();

                using NativeResource<FpdfPageT> page = LoadPageOnWorker(_documentResource!.Handle, pageIndex);
                IReadOnlyList<PixelRect> imageBounds = CollectImageBoundsOnWorker(page.Handle, targetSize);

                using NativeResource<FpdfBitmapT> bitmap = CreateBitmapOnWorker(targetSize.Width, targetSize.Height);

                fpdfview.FPDFBitmapFillRect(bitmap.Handle, 0, 0, targetSize.Width, targetSize.Height, 0xFFFFFFFF);
                fpdfview.FPDF_RenderPageBitmap(
                    bitmap.Handle,
                    page.Handle,
                    0,
                    0,
                    targetSize.Width,
                    targetSize.Height,
                    0,
                    (int)RenderFlags.RenderAnnotations);

                cancellationToken.ThrowIfCancellationRequested();

                IntPtr bufferPtr = fpdfview.FPDFBitmapGetBuffer(bitmap.Handle);
                int srcStride = fpdfview.FPDFBitmapGetStride(bitmap.Handle);
                if (bufferPtr == IntPtr.Zero || srcStride <= 0)
                {
                    throw new InvalidOperationException($"PDFium returned an invalid bitmap buffer for page {pageIndex}.");
                }

                int dstStride = checked(targetSize.Width * 4);
                byte[] pixels = new byte[checked(dstStride * targetSize.Height)];

                if (srcStride == dstStride)
                {
                    Marshal.Copy(bufferPtr, pixels, 0, pixels.Length);
                }
                else
                {
                    for (int row = 0; row < targetSize.Height; row++)
                    {
                        IntPtr rowPtr = IntPtr.Add(bufferPtr, row * srcStride);
                        Marshal.Copy(rowPtr, pixels, row * dstStride, dstStride);
                    }
                }

                return new RenderedPageBuffer(pageIndex, targetSize, dstStride, pixels, imageBounds);
            },
            cancellationToken);
    }

    public Task<string> GetPageTextAsync(int pageIndex, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);

        return _worker.RunAsync(
            () =>
            {
                EnsureOpenPageIndexOnWorker(pageIndex);
                cancellationToken.ThrowIfCancellationRequested();

                using NativeResource<FpdfPageT> page = LoadPageOnWorker(_documentResource!.Handle, pageIndex);
                using NativeResource<FpdfTextpageT> textPage = LoadTextPageOnWorker(page.Handle, pageIndex);

                int charCount = fpdf_text.FPDFTextCountChars(textPage.Handle);
                if (charCount <= 0)
                {
                    return string.Empty;
                }

                return ExtractTextSegmentOnWorker(textPage.Handle, 0, charCount);
            },
            cancellationToken);
    }

    public Task<PdfTextSelection?> SelectTextAsync(
        int pageIndex,
        double startNormalizedX,
        double startNormalizedY,
        double endNormalizedX,
        double endNormalizedY,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);

        double clampedStartX = Math.Clamp(startNormalizedX, 0.0, 1.0);
        double clampedStartY = Math.Clamp(startNormalizedY, 0.0, 1.0);
        double clampedEndX = Math.Clamp(endNormalizedX, 0.0, 1.0);
        double clampedEndY = Math.Clamp(endNormalizedY, 0.0, 1.0);

        return _worker.RunAsync(
            () =>
            {
                EnsureOpenPageIndexOnWorker(pageIndex);
                cancellationToken.ThrowIfCancellationRequested();

                using NativeResource<FpdfPageT> page = LoadPageOnWorker(_documentResource!.Handle, pageIndex);
                using NativeResource<FpdfTextpageT> textPage = LoadTextPageOnWorker(page.Handle, pageIndex);

                int charCount = fpdf_text.FPDFTextCountChars(textPage.Handle);
                if (charCount <= 0)
                {
                    return null;
                }

                if (!TryNormalizedDeviceToPageOnWorker(page.Handle, clampedStartX, clampedStartY, out double startPdfX, out double startPdfY) ||
                    !TryNormalizedDeviceToPageOnWorker(page.Handle, clampedEndX, clampedEndY, out double endPdfX, out double endPdfY))
                {
                    return null;
                }

                int startIndex = FindCharIndexNearPointOnWorker(textPage.Handle, charCount, startPdfX, startPdfY);
                int endIndex = FindCharIndexNearPointOnWorker(textPage.Handle, charCount, endPdfX, endPdfY);
                if (startIndex < 0 && endIndex < 0)
                {
                    return null;
                }

                if (startIndex < 0)
                {
                    startIndex = endIndex;
                }
                else if (endIndex < 0)
                {
                    endIndex = startIndex;
                }

                int firstChar = Math.Min(startIndex, endIndex);
                int lastChar = Math.Max(startIndex, endIndex);
                int selectedCount = lastChar - firstChar + 1;

                string selectedText = ExtractTextSegmentOnWorker(textPage.Handle, firstChar, selectedCount);
                if (string.IsNullOrWhiteSpace(selectedText))
                {
                    return null;
                }

                IReadOnlyList<NormalizedRect> rects = ExtractRangeRectsOnWorker(
                    page.Handle,
                    textPage.Handle,
                    firstChar,
                    selectedCount);

                return new PdfTextSelection(pageIndex, firstChar, selectedCount, selectedText, rects);
            },
            cancellationToken);
    }

    public Task<IReadOnlyList<PdfSearchMatch>> SearchDocumentAsync(
        string query,
        bool matchCase = false,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);

        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult<IReadOnlyList<PdfSearchMatch>>(Array.Empty<PdfSearchMatch>());
        }

        string trimmedQuery = query.Trim();

        return _worker.RunAsync<IReadOnlyList<PdfSearchMatch>>(
            () =>
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                if (_documentResource is null || _currentDocument is null)
                {
                    throw new InvalidOperationException("No PDF document is currently open.");
                }

                ushort[] queryBuffer = new ushort[trimmedQuery.Length + 1];
                for (int i = 0; i < trimmedQuery.Length; i++)
                {
                    queryBuffer[i] = trimmedQuery[i];
                }

                queryBuffer[trimmedQuery.Length] = 0;
                ulong flags = matchCase ? FpdfMatchCaseFlag : 0UL;

                var matches = new List<PdfSearchMatch>();
                for (int pageIndex = 0; pageIndex < _currentDocument.PageCount; pageIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    using NativeResource<FpdfPageT> page = LoadPageOnWorker(_documentResource.Handle, pageIndex);
                    using NativeResource<FpdfTextpageT> textPage = LoadTextPageOnWorker(page.Handle, pageIndex);

                    FpdfSchhandleT? rawSearch = fpdf_text.FPDFTextFindStart(textPage.Handle, ref queryBuffer[0], flags, 0);
                    if (rawSearch is null || rawSearch.__Instance == IntPtr.Zero)
                    {
                        continue;
                    }

                    using var searchResource = new NativeResource<FpdfSchhandleT>(rawSearch, fpdf_text.FPDFTextFindClose);
                    while (fpdf_text.FPDFTextFindNext(searchResource.Handle) != 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        int matchStart = fpdf_text.FPDFTextGetSchResultIndex(searchResource.Handle);
                        int matchCount = fpdf_text.FPDFTextGetSchCount(searchResource.Handle);
                        if (matchStart >= 0 && matchCount > 0)
                        {
                            IReadOnlyList<NormalizedRect> rects = ExtractRangeRectsOnWorker(
                                page.Handle,
                                textPage.Handle,
                                matchStart,
                                matchCount);

                            matches.Add(new PdfSearchMatch(pageIndex, matchStart, matchCount, rects));
                        }
                    }
                }

                return matches.Count == 0 ? Array.Empty<PdfSearchMatch>() : matches.AsReadOnly();
            },
            cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Volatile.Read(ref _disposed))
        {
            return;
        }

        Volatile.Write(ref _disposed, true);

        try
        {
            await _worker.RunCleanupAsync(() =>
            {
                CloseDocumentOnWorker();
                ReleaseLibraryOnWorker();
            }).ConfigureAwait(false);
        }
        finally
        {
            if (_ownsWorker)
            {
                await _worker.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private void EnsureOpenPageIndexOnWorker(int pageIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_documentResource is null || _currentDocument is null)
        {
            throw new InvalidOperationException("No PDF document is currently open.");
        }

        if (pageIndex < 0 || pageIndex >= _currentDocument.PageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex), "Page index is out of range.");
        }
    }

    private static string ExtractTextSegmentOnWorker(FpdfTextpageT textPage, int startIndex, int count)
    {
        if (count <= 0)
        {
            return string.Empty;
        }

        ushort[] buffer = new ushort[checked(count + 1)];
        int written = fpdf_text.FPDFTextGetText(textPage, startIndex, count, ref buffer[0]);
        if (written <= 1)
        {
            return string.Empty;
        }

        int charLength = Math.Min(written - 1, count);
        ReadOnlySpan<char> chars = MemoryMarshal.Cast<ushort, char>(buffer.AsSpan(0, charLength));
        return new string(chars).TrimEnd('\0');
    }

    private static IReadOnlyList<NormalizedRect> ExtractRangeRectsOnWorker(
        FpdfPageT page,
        FpdfTextpageT textPage,
        int startIndex,
        int count)
    {
        int rectCount = fpdf_text.FPDFTextCountRects(textPage, startIndex, count);
        if (rectCount <= 0)
        {
            return Array.Empty<NormalizedRect>();
        }

        var rects = new List<NormalizedRect>(rectCount);
        for (int i = 0; i < rectCount; i++)
        {
            double left = 0;
            double top = 0;
            double right = 0;
            double bottom = 0;
            if (fpdf_text.FPDFTextGetRect(textPage, i, ref left, ref top, ref right, ref bottom) != 0)
            {
                int x1 = 0;
                int y1 = 0;
                int x2 = 0;
                int y2 = 0;
                fpdfview.FPDF_PageToDevice(page, 0, 0, ReferenceDeviceScale, ReferenceDeviceScale, 0, left, top, ref x1, ref y1);
                fpdfview.FPDF_PageToDevice(page, 0, 0, ReferenceDeviceScale, ReferenceDeviceScale, 0, right, bottom, ref x2, ref y2);

                NormalizedRect norm = NormalizedRect.FromPoints(
                    x1 / (double)ReferenceDeviceScale,
                    y1 / (double)ReferenceDeviceScale,
                    x2 / (double)ReferenceDeviceScale,
                    y2 / (double)ReferenceDeviceScale);

                if (!norm.IsEmpty)
                {
                    rects.Add(norm);
                }
            }
        }

        return rects.Count == 0 ? Array.Empty<NormalizedRect>() : rects.AsReadOnly();
    }

    private static bool TryNormalizedDeviceToPageOnWorker(
        FpdfPageT page,
        double normalizedX,
        double normalizedY,
        out double pageX,
        out double pageY)
    {
        int devX = (int)Math.Round(Math.Clamp(normalizedX, 0.0, 1.0) * ReferenceDeviceScale);
        int devY = (int)Math.Round(Math.Clamp(normalizedY, 0.0, 1.0) * ReferenceDeviceScale);
        pageX = 0;
        pageY = 0;
        return fpdfview.FPDF_DeviceToPage(
            page,
            0,
            0,
            ReferenceDeviceScale,
            ReferenceDeviceScale,
            0,
            devX,
            devY,
            ref pageX,
            ref pageY) != 0;
    }

    private static int FindCharIndexNearPointOnWorker(
        FpdfTextpageT textPage,
        int charCount,
        double pdfX,
        double pdfY)
    {
        int directIndex = fpdf_text.FPDFTextGetCharIndexAtPos(
            textPage,
            pdfX,
            pdfY,
            HitTestDirectTolerancePoints,
            HitTestDirectTolerancePoints);

        if (directIndex >= 0)
        {
            return directIndex;
        }

        int closestIndex = -1;
        double closestDistanceSquared = HitTestFallbackTolerancePoints * HitTestFallbackTolerancePoints;

        for (int i = 0; i < charCount; i++)
        {
            double left = 0;
            double right = 0;
            double bottom = 0;
            double top = 0;
            if (fpdf_text.FPDFTextGetCharBox(textPage, i, ref left, ref right, ref bottom, ref top) == 0)
            {
                continue;
            }

            double minX = Math.Min(left, right);
            double maxX = Math.Max(left, right);
            double minY = Math.Min(bottom, top);
            double maxY = Math.Max(bottom, top);

            if (maxX <= minX && maxY <= minY)
            {
                continue;
            }

            double dx = pdfX < minX ? minX - pdfX : (pdfX > maxX ? pdfX - maxX : 0.0);
            double dy = pdfY < minY ? minY - pdfY : (pdfY > maxY ? pdfY - maxY : 0.0);
            double distSquared = (dx * dx) + (dy * dy);
            if (distSquared <= closestDistanceSquared)
            {
                closestDistanceSquared = distSquared;
                closestIndex = i;
            }
        }

        return closestIndex;
    }

    private static IReadOnlyList<PixelRect> CollectImageBoundsOnWorker(FpdfPageT page, PixelSize targetSize)
    {
        int objectCount = fpdf_edit.FPDFPageCountObjects(page);
        if (objectCount <= 0)
        {
            return Array.Empty<PixelRect>();
        }

        var bounds = new List<PixelRect>();
        for (int i = 0; i < objectCount; i++)
        {
            FpdfPageobjectT? pageObj = fpdf_edit.FPDFPageGetObject(page, i);
            CollectPageObjectImageBoundsOnWorker(page, pageObj, targetSize, bounds, depth: 0);
        }

        return bounds.Count == 0 ? Array.Empty<PixelRect>() : bounds.AsReadOnly();
    }

    private static void CollectPageObjectImageBoundsOnWorker(
        FpdfPageT page,
        FpdfPageobjectT? pageObj,
        PixelSize targetSize,
        List<PixelRect> bounds,
        int depth)
    {
        if (pageObj is null || pageObj.__Instance == IntPtr.Zero || depth > MaxFormRecursionDepth)
        {
            return;
        }

        int objType = fpdf_edit.FPDFPageObjGetType(pageObj);
        if (objType == FpdfPageObjImage)
        {
            float left = 0;
            float bottom = 0;
            float right = 0;
            float top = 0;
            if (fpdf_edit.FPDFPageObjGetBounds(pageObj, ref left, ref bottom, ref right, ref top) != 0)
            {
                int x1 = 0;
                int y1 = 0;
                int x2 = 0;
                int y2 = 0;
                fpdfview.FPDF_PageToDevice(page, 0, 0, targetSize.Width, targetSize.Height, 0, left, top, ref x1, ref y1);
                fpdfview.FPDF_PageToDevice(page, 0, 0, targetSize.Width, targetSize.Height, 0, right, bottom, ref x2, ref y2);

                int minX = Math.Min(x1, x2);
                int maxX = Math.Max(x1, x2);
                int minY = Math.Min(y1, y2);
                int maxY = Math.Max(y1, y2);

                PixelRect rect = new PixelRect(minX, minY, maxX - minX, maxY - minY).Clamp(targetSize);
                if (!rect.IsEmpty)
                {
                    bounds.Add(rect);
                }
            }
        }
        else if (objType == FpdfPageObjForm)
        {
            int childCount = fpdf_edit.FPDFFormObjCountObjects(pageObj);
            for (int i = 0; i < childCount; i++)
            {
                FpdfPageobjectT? childObj = fpdf_edit.FPDFFormObjGetObject(pageObj, (ulong)i);
                CollectPageObjectImageBoundsOnWorker(page, childObj, targetSize, bounds, depth + 1);
            }
        }
    }

    private static NativeResource<FpdfPageT> LoadPageOnWorker(FpdfDocumentT document, int pageIndex)
    {
        FpdfPageT? rawPage = fpdfview.FPDF_LoadPage(document, pageIndex);
        if (rawPage is null || rawPage.__Instance == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Failed to load PDF page {pageIndex}.");
        }

        return new NativeResource<FpdfPageT>(rawPage, fpdfview.FPDF_ClosePage);
    }

    private static NativeResource<FpdfTextpageT> LoadTextPageOnWorker(FpdfPageT page, int pageIndex)
    {
        FpdfTextpageT? rawTextPage = fpdf_text.FPDFTextLoadPage(page);
        if (rawTextPage is null || rawTextPage.__Instance == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Failed to load PDF text page {pageIndex}.");
        }

        return new NativeResource<FpdfTextpageT>(rawTextPage, fpdf_text.FPDFTextClosePage);
    }

    private static NativeResource<FpdfBitmapT> CreateBitmapOnWorker(int width, int height)
    {
        FpdfBitmapT? rawBitmap = fpdfview.FPDFBitmapCreate(width, height, 1);
        if (rawBitmap is null || rawBitmap.__Instance == IntPtr.Zero)
        {
            throw new InvalidOperationException($"Failed to allocate PDFium bitmap ({width}x{height}).");
        }

        return new NativeResource<FpdfBitmapT>(rawBitmap, fpdfview.FPDFBitmapDestroy);
    }

    private void CloseDocumentOnWorker()
    {
        Volatile.Write(ref _currentDocument, null);
        _documentResource?.Dispose();
        _documentResource = null;
    }

    private void EnsureLibraryInitializedOnWorker()
    {
        if (_libraryInitialized)
        {
            return;
        }

        if (Interlocked.Increment(ref s_libraryRefCount) == 1)
        {
            fpdfview.FPDF_InitLibrary();
        }

        _libraryInitialized = true;
    }

    private void ReleaseLibraryOnWorker()
    {
        if (!_libraryInitialized)
        {
            return;
        }

        _libraryInitialized = false;
        if (Interlocked.Decrement(ref s_libraryRefCount) == 0)
        {
            fpdfview.FPDF_DestroyLibrary();
        }
    }
}
