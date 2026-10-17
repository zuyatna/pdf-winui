namespace PdfReader.Core;

public sealed record RenderedPageBuffer(
    int PageIndex,
    PixelSize Size,
    int Stride,
    byte[] BgraPixels)
{
    public int ByteLength => BgraPixels.Length;
}
