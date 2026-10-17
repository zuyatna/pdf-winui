using System;

namespace PdfReader.Core;

public sealed record PdfBookmarkItem(
    int PageNumber,
    string Title,
    DateTimeOffset CreatedUtc);
