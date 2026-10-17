using System;
using System.Collections.Generic;

namespace PdfReader.Core;

public sealed record RenderedPageBuffer(
    int PageIndex,
    PixelSize Size,
    int Stride,
    byte[] BgraPixels,
    IReadOnlyList<PixelRect>? ImageBounds = null)
{
    public int ByteLength => BgraPixels.Length;

    public IReadOnlyList<PixelRect> EffectiveImageBounds => ImageBounds ?? Array.Empty<PixelRect>();

    public bool IsScannedPage => ScannedPageDetector.IsScannedPage(Size, ImageBounds);
}
