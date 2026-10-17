using System.Runtime.InteropServices;
using System.Text;
using PDFiumCore;

namespace PdfReader.Core;

public sealed partial class PdfDocumentService
{
    private const int MaxOutlineDepth = 64;
    private const int MaxOutlineItems = 10000;
    private const ulong MaxOutlineTitleBytes = 65536;

    public Task<IReadOnlyList<PdfOutlineItem>> GetOutlineAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);
        return _worker.RunAsync<IReadOnlyList<PdfOutlineItem>>(() =>
        {
            EnsureOpenPageIndexOnWorker(0);
            cancellationToken.ThrowIfCancellationRequested();
            return ReadOutlineOnWorker(_documentResource!.Handle, null, 0, [], cancellationToken);
        }, cancellationToken);
    }

    private IReadOnlyList<PdfOutlineItem> ReadOutlineOnWorker(
        FpdfDocumentT document,
        FpdfBookmarkT? parent,
        int level,
        HashSet<IntPtr> visited,
        CancellationToken cancellationToken)
    {
        var items = new List<PdfOutlineItem>();
        FpdfBookmarkT? bookmark = fpdf_doc.FPDFBookmarkGetFirstChild(document, parent);
        while (bookmark is not null && bookmark.__Instance != IntPtr.Zero && visited.Count < MaxOutlineItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(bookmark.__Instance))
            {
                break;
            }

            // Outline, action and destination handles are borrowed from the document.
            string title = ReadOutlineTitleOnWorker(bookmark);
            PdfPageDestination? destination = ReadOutlineDestinationOnWorker(document, bookmark);
            IReadOnlyList<PdfOutlineItem> children = level < MaxOutlineDepth
                ? ReadOutlineOnWorker(document, bookmark, level + 1, visited, cancellationToken)
                : [];
            items.Add(new PdfOutlineItem(title, destination?.PageIndex, level, children,
                destination?.NormalizedX, destination?.NormalizedY));
            bookmark = fpdf_doc.FPDFBookmarkGetNextSibling(document, bookmark);
        }

        return items.AsReadOnly();
    }

    private static string ReadOutlineTitleOnWorker(FpdfBookmarkT bookmark)
    {
        ulong length = fpdf_doc.FPDFBookmarkGetTitle(bookmark, IntPtr.Zero, 0);
        if (length <= 2 || length > MaxOutlineTitleBytes || length % 2 != 0)
        {
            return string.Empty;
        }

        IntPtr buffer = Marshal.AllocHGlobal(checked((int)length));
        try
        {
            ulong written = fpdf_doc.FPDFBookmarkGetTitle(bookmark, buffer, length);
            if (written != length)
            {
                return string.Empty;
            }

            var bytes = new byte[checked((int)length - 2)];
            Marshal.Copy(buffer, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private PdfPageDestination? ReadOutlineDestinationOnWorker(FpdfDocumentT document, FpdfBookmarkT bookmark)
    {
        FpdfActionT? action = fpdf_doc.FPDFBookmarkGetAction(bookmark);
        bool hasAction = action is not null && action.__Instance != IntPtr.Zero;
        // PDFium may also resolve remote action destinations through GetDest.
        // Check the action before interpreting any index as a local page.
        if (hasAction && fpdf_doc.FPDFActionGetType(action) != 1)
        {
            return null;
        }

        FpdfDestT? destination = hasAction
            ? fpdf_doc.FPDFActionGetDest(document, action)
            : fpdf_doc.FPDFBookmarkGetDest(document, bookmark);

        if (destination is null || destination.__Instance == IntPtr.Zero)
        {
            return null;
        }

        int pageIndex = fpdf_doc.FPDFDestGetDestPageIndex(document, destination);
        return pageIndex >= 0 && pageIndex < _currentDocument!.PageCount
            ? ReadDestinationPositionOnWorker(document, destination, pageIndex)
            : null;
    }

    private static PdfPageDestination ReadDestinationPositionOnWorker(FpdfDocumentT document, FpdfDestT destination, int pageIndex)
    {
        (double? x, double? y) = ReadDestinationCoordinatesOnWorker(destination);
        if (!x.HasValue && !y.HasValue)
        {
            return new(pageIndex);
        }

        using NativeResource<FpdfPageT> page = LoadPageOnWorker(document, pageIndex);
        int deviceX = 0, deviceY = 0;
        if (fpdfview.FPDF_PageToDevice(page.Handle, 0, 0, ReferenceDeviceScale, ReferenceDeviceScale, 0,
            x ?? 0, y ?? 0, ref deviceX, ref deviceY) == 0)
        {
            return new(pageIndex);
        }

        // PageToDevice applies the same intrinsic rotation and crop box as bitmap rendering.
        bool swapsAxes = fpdf_edit.FPDFPageGetRotation(page.Handle) % 2 != 0;
        double? normalizedX = (swapsAxes ? y.HasValue : x.HasValue)
            ? Math.Clamp(deviceX / (double)ReferenceDeviceScale, 0, 1) : null;
        double? normalizedY = (swapsAxes ? x.HasValue : y.HasValue)
            ? Math.Clamp(deviceY / (double)ReferenceDeviceScale, 0, 1) : null;
        return new(pageIndex, normalizedX, normalizedY);
    }

    private static (double? X, double? Y) ReadDestinationCoordinatesOnWorker(FpdfDestT destination)
    {
        int hasX = 0, hasY = 0, hasZoom = 0;
        float x = 0, y = 0, zoom = 0;
        if (fpdf_doc.FPDFDestGetLocationInPage(destination, ref hasX, ref hasY, ref hasZoom, ref x, ref y, ref zoom) != 0)
        {
            return (hasX != 0 && float.IsFinite(x) ? x : null, hasY != 0 && float.IsFinite(y) ? y : null);
        }

        ulong count = 0;
        var parameters = new float[4];
        ulong view = fpdf_doc.FPDFDestGetView(destination, ref count, ref parameters[0]);
        double? left = view switch
        {
            4 or 8 when count >= 1 => parameters[0], // FitV / FitBV
            5 when count == 4 => parameters[0], // FitR
            _ => null,
        };
        double? top = view switch
        {
            3 or 7 when count >= 1 => parameters[0], // FitH / FitBH
            5 when count == 4 => parameters[3], // FitR
            _ => null,
        };
        return (left is double leftValue && double.IsFinite(leftValue) ? leftValue : null,
            top is double topValue && double.IsFinite(topValue) ? topValue : null);
    }
}
