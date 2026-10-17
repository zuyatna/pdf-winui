using System;

namespace PdfReader.Core;

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public long Area => Width > 0 && Height > 0 ? (long)Width * Height : 0L;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public PixelRect Clamp(PixelSize pageSize)
    {
        if (pageSize.Width <= 0 || pageSize.Height <= 0 || IsEmpty)
        {
            return default;
        }

        int left = Math.Clamp(X, 0, pageSize.Width);
        int top = Math.Clamp(Y, 0, pageSize.Height);
        int right = Math.Clamp(X + Width, 0, pageSize.Width);
        int bottom = Math.Clamp(Y + Height, 0, pageSize.Height);

        int clampedWidth = Math.Max(0, right - left);
        int clampedHeight = Math.Max(0, bottom - top);
        return new PixelRect(left, top, clampedWidth, clampedHeight);
    }
}
