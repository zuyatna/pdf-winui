using System;

namespace PdfReader.Core;

public sealed record ReadingThemePalette(
    ReadingThemeMode Mode,
    RgbColor PageBackground,
    RgbColor Text,
    RgbColor Canvas,
    double ImageDimAmount = 0.10)
{
    public bool IsRemapEnabled => Mode != ReadingThemeMode.Original;

    public double ClampedImageDimAmount => Math.Clamp(ImageDimAmount, 0.0, 0.80);

    public static ReadingThemePalette Original { get; } = new(
        ReadingThemeMode.Original,
        RgbColor.White,
        RgbColor.Black,
        new RgbColor(0xF3, 0xF3, 0xF3),
        ImageDimAmount: 0.0);

    public static ReadingThemePalette DefaultDark { get; } = new(
        ReadingThemeMode.Dark,
        RgbColor.DefaultDarkPageBackground,
        RgbColor.DefaultDarkText,
        RgbColor.DefaultDarkCanvas,
        ImageDimAmount: 0.10);
}
