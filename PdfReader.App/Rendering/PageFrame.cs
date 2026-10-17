using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PdfReader.Core;

namespace PdfReader.App.Rendering;

public sealed class PageFrame : IDisposable
{
    private byte[]? _bgraPixels;

    public PageFrame(int pageIndex, PixelSize size, int stride, byte[] bgraPixels)
    {
        ArgumentNullException.ThrowIfNull(bgraPixels);

        if (size.Width <= 0 || size.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Frame dimensions must be positive.");
        }

        if (stride < size.Width * 4)
        {
            throw new ArgumentOutOfRangeException(nameof(stride), "Stride is smaller than pixel row width.");
        }

        PageIndex = pageIndex;
        Size = size;
        Stride = stride;
        ByteSize = bgraPixels.LongLength;
        _bgraPixels = bgraPixels;
    }

    public int PageIndex { get; }

    public PixelSize Size { get; }

    public int Stride { get; }

    public long ByteSize { get; }

    public bool IsDisposed => _bgraPixels is null;

    public ImageSource CreateImageSource()
    {
        byte[] pixels = _bgraPixels ?? throw new ObjectDisposedException(nameof(PageFrame));

        var bitmap = new WriteableBitmap(Size.Width, Size.Height);
        using Stream pixelStream = bitmap.PixelBuffer.AsStream();
        pixelStream.Write(pixels, 0, pixels.Length);
        bitmap.Invalidate();
        return bitmap;
    }

    public void Dispose()
    {
        _bgraPixels = null;
    }
}
