using System.Collections.Generic;

namespace PdfReader.Core;

public sealed record PdfSearchMatch(
    int PageIndex,
    int StartCharIndex,
    int CharCount,
    IReadOnlyList<NormalizedRect> HighlightRects);
