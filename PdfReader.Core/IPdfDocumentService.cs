using System;
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
}
