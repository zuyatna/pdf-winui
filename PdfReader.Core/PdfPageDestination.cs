namespace PdfReader.Core;

public sealed record PdfPageDestination(int PageIndex, double? NormalizedX = null, double? NormalizedY = null)
{
    public double GetVerticalOffset(double currentOffset, double pageTop, double pageHeight, double scrollableHeight)
    {
        double y = NormalizedY is double value && double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
        double headingMargin = NormalizedY.HasValue ? 24 : 0;
        return Math.Clamp(currentOffset + pageTop + y * pageHeight - headingMargin, 0, Math.Max(0, scrollableHeight));
    }

    public double? GetHorizontalOffset(double currentOffset, double pageLeft, double pageWidth, double scrollableWidth)
    {
        return NormalizedX is double x && double.IsFinite(x)
            ? Math.Clamp(currentOffset + pageLeft + Math.Clamp(x, 0, 1) * pageWidth - 24, 0, Math.Max(0, scrollableWidth))
            : null;
    }
}
