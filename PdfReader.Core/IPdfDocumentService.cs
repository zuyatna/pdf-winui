using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PdfReader.Core;

public interface IPdfDocumentService : IAsyncDisposable
{
    PdfDocumentMetadata? CurrentDocument { get; }

    Task<PdfDocumentMetadata> OpenDocumentAsync(string filePath, CancellationToken cancellationToken = default);

    Task CloseDocumentAsync(CancellationToken cancellationToken = default);

    Task<RenderedPageBuffer> RenderPageAsync(
        int pageIndex,
        PixelSize targetSize,
        CancellationToken cancellationToken = default);

    Task<string> GetPageTextAsync(int pageIndex, CancellationToken cancellationToken = default);

    Task<PdfTextSelection?> SelectTextAsync(
        int pageIndex,
        double startNormalizedX,
        double startNormalizedY,
        double endNormalizedX,
        double endNormalizedY,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PdfSearchMatch>> SearchDocumentAsync(
        string query,
        bool matchCase = false,
        CancellationToken cancellationToken = default);
}
