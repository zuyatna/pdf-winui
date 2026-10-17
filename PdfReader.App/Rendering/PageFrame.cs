using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PdfReader.Core;

namespace PdfReader.App.Rendering;

public sealed class PageFrame : IDisposable
{
    private readonly object _gate = new();
    private byte[]? _bgraPixels;
    private ImageSource? _imageSource;
    private bool _disposed;

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

    public bool IsDisposed
    {
        get
        {
            lock (_gate)
            {
                return _disposed;
            }
        }
    }

    // Called on the UI thread; eviction can dispose the frame from a render thread.
    public bool TryCreateImageSource([NotNullWhen(true)] out ImageSource? imageSource)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                imageSource = null;
                return false;
            }

            if (_imageSource is null)
            {
                byte[] pixels = _bgraPixels ?? throw new InvalidOperationException("Frame pixels are unavailable.");
                var bitmap = new WriteableBitmap(Size.Width, Size.Height);
                using Stream pixelStream = bitmap.PixelBuffer.AsStream();
                pixelStream.Write(pixels, 0, pixels.Length);
                bitmap.Invalidate();
                _imageSource = bitmap;
                _bgraPixels = null;
            }

            imageSource = _imageSource;
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _bgraPixels = null;
            _imageSource = null;
        }
    }
}
