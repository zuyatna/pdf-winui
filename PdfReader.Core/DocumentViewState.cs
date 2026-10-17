using System;

namespace PdfReader.Core;

public sealed record DocumentViewState(
    string FilePath,
    int LastPageNumber,
    double Zoom,
    ZoomMode ZoomMode,
    DateTimeOffset LastOpenedUtc);
