using System.Collections.Generic;

namespace PdfReader.Core;

public sealed record PdfDocumentMetadata(
    string FilePath,
    int PageCount,
    IReadOnlyList<PdfPageSize> PageSizes);
