using System;

namespace PdfReader.Core;

public readonly record struct PixelSize(int Width, int Height)
{
    private const double DefaultDpi = 96.0;
    private const double PointsPerInch = 72.0;

    public int ByteLength => checked(Width * Height * 4);

    public static PixelSize FromPageSize(PdfPageSize pageSize, double scale, double dpi = DefaultDpi)
    {
        if (pageSize.WidthPoints <= 0 || !double.IsFinite(pageSize.WidthPoints))
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Page width must be positive and finite.");

        if (pageSize.HeightPoints <= 0 || !double.IsFinite(pageSize.HeightPoints))
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Page height must be positive and finite.");

        if (scale <= 0 || !double.IsFinite(scale))
            throw new ArgumentOutOfRangeException(nameof(scale), "Scale must be positive and finite.");

        if (dpi <= 0 || !double.IsFinite(dpi))
            throw new ArgumentOutOfRangeException(nameof(dpi), "DPI must be positive and finite.");

        double factor = (dpi / PointsPerInch) * scale;
        int width = Math.Max(1, (int)Math.Round(pageSize.WidthPoints * factor, MidpointRounding.AwayFromZero));
        int height = Math.Max(1, (int)Math.Round(pageSize.HeightPoints * factor, MidpointRounding.AwayFromZero));

        return new PixelSize(width, height);
    }
}
