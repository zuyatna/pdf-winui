using System;

namespace PdfReader.Core;

public readonly record struct NormalizedRect(double X, double Y, double Width, double Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static NormalizedRect FromPoints(double x1, double y1, double x2, double y2)
    {
        double left = Math.Clamp(Math.Min(x1, x2), 0.0, 1.0);
        double top = Math.Clamp(Math.Min(y1, y2), 0.0, 1.0);
        double right = Math.Clamp(Math.Max(x1, x2), 0.0, 1.0);
        double bottom = Math.Clamp(Math.Max(y1, y2), 0.0, 1.0);

        return new NormalizedRect(
            left,
            top,
            Math.Max(0.0, right - left),
            Math.Max(0.0, bottom - top));
    }
}
