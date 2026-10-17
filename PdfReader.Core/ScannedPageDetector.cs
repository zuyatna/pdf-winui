using System;
using System.Collections.Generic;

namespace PdfReader.Core;

public static class ScannedPageDetector
{
    public const double DefaultThreshold = 0.85;

    public static double CalculateImageCoverage(PixelSize pageSize, IReadOnlyList<PixelRect>? imageBounds)
    {
        if (pageSize.Width <= 0 || pageSize.Height <= 0 || imageBounds is null || imageBounds.Count == 0)
        {
            return 0.0;
        }

        double pageArea = (double)pageSize.Width * pageSize.Height;
        long coveredArea = 0;

        for (int i = 0; i < imageBounds.Count; i++)
        {
            PixelRect clamped = imageBounds[i].Clamp(pageSize);
            coveredArea += clamped.Area;
        }

        return Math.Clamp(coveredArea / pageArea, 0.0, 1.0);
    }

    public static bool IsScannedPage(
        PixelSize pageSize,
        IReadOnlyList<PixelRect>? imageBounds,
        double threshold = DefaultThreshold)
    {
        if (threshold <= 0 || threshold > 1.0 || !double.IsFinite(threshold))
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), "Threshold must be in the range (0, 1].");
        }

        return CalculateImageCoverage(pageSize, imageBounds) >= threshold;
    }
}
