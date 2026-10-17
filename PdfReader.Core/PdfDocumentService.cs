using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using PDFiumCore;

namespace PdfReader.Core;

public sealed class PdfDocumentService : IPdfDocumentService
{
    private const int FpdfPageObjImage = 3;
    private const int FpdfPageObjForm = 5;
    private const int MaxFormRecursionDepth = 8;

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
                ObjectDisposedException.ThrowIf(_disposed, this);

                if (_documentResource is null || _currentDocument is null)
                {
                    throw new InvalidOperationException("No PDF document is currently open.");
                }

                if (pageIndex < 0 || pageIndex >= _currentDocument.PageCount)
                {
                    throw new ArgumentOutOfRangeException(nameof(pageIndex), "Page index is out of range.");
                }

                cancellationToken.ThrowIfCancellationRequested();

                using NativeResource<FpdfPageT> page = LoadPageOnWorker(_documentResource.Handle, pageIndex);
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
