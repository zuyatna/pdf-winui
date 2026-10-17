using System.Collections.Generic;

namespace PdfReader.Core;

public sealed record PdfTextSelection(
    int PageIndex,
    int StartCharIndex,
    int CharCount,
    string SelectedText,
    IReadOnlyList<NormalizedRect> HighlightRects);
