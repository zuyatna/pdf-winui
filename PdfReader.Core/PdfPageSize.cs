namespace PdfReader.Core;

public readonly record struct PdfPageSize(double WidthPoints, double HeightPoints)
{
    private const double DipPerPoint = 96.0 / 72.0;

    public double WidthDip => WidthPoints * DipPerPoint;

    public double HeightDip => HeightPoints * DipPerPoint;

    public double AspectRatio => HeightPoints > 0 ? WidthPoints / HeightPoints : 1.0;
}
