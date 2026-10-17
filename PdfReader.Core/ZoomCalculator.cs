using System;

namespace PdfReader.Core;

public static class ZoomCalculator
{
    public const double MinZoom = 0.25;
    public const double MaxZoom = 4.00;
    public const double DefaultStep = 0.10;

    public static double Clamp(double zoom)
    {
        if (!double.IsFinite(zoom))
        {
            return 1.0;
        }

        return Math.Clamp(zoom, MinZoom, MaxZoom);
    }

    public static double StepZoom(double currentZoom, int steps, double stepDelta = DefaultStep)
    {
        if (!double.IsFinite(currentZoom))
        {
            currentZoom = 1.0;
        }

        if (stepDelta <= 0 || !double.IsFinite(stepDelta))
        {
            throw new ArgumentOutOfRangeException(nameof(stepDelta), "Step delta must be positive and finite.");
        }

        double raw = currentZoom + (steps * stepDelta);
        double rounded = Math.Round(raw, 2, MidpointRounding.AwayFromZero);
        return Clamp(rounded);
    }

    public static double CalculateFitWidthZoom(
        PdfPageSize pageSize,
        double viewportWidthDip,
        double horizontalPaddingDip = 48.0)
    {
        if (pageSize.WidthPoints <= 0 || !double.IsFinite(pageSize.WidthPoints))
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Page width must be positive.");
        }

        if (viewportWidthDip <= 0 || !double.IsFinite(viewportWidthDip))
        {
            throw new ArgumentOutOfRangeException(nameof(viewportWidthDip), "Viewport width must be positive.");
        }

        double availableWidth = Math.Max(1.0, viewportWidthDip - Math.Max(0.0, horizontalPaddingDip));
        return Clamp(availableWidth / pageSize.WidthDip);
    }

    public static double CalculateFitPageZoom(
        PdfPageSize pageSize,
        double viewportWidthDip,
        double viewportHeightDip,
        double horizontalPaddingDip = 48.0,
        double verticalPaddingDip = 32.0)
    {
        if (pageSize.WidthPoints <= 0 || pageSize.HeightPoints <= 0 ||
            !double.IsFinite(pageSize.WidthPoints) || !double.IsFinite(pageSize.HeightPoints))
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Page dimensions must be positive.");
        }

        if (viewportWidthDip <= 0 || !double.IsFinite(viewportWidthDip))
        {
            throw new ArgumentOutOfRangeException(nameof(viewportWidthDip), "Viewport width must be positive.");
        }

        if (viewportHeightDip <= 0 || !double.IsFinite(viewportHeightDip))
        {
            throw new ArgumentOutOfRangeException(nameof(viewportHeightDip), "Viewport height must be positive.");
        }

        double availableWidth = Math.Max(1.0, viewportWidthDip - Math.Max(0.0, horizontalPaddingDip));
        double availableHeight = Math.Max(1.0, viewportHeightDip - Math.Max(0.0, verticalPaddingDip));

        double widthZoom = availableWidth / pageSize.WidthDip;
        double heightZoom = availableHeight / pageSize.HeightDip;
        return Clamp(Math.Min(widthZoom, heightZoom));
    }
}
