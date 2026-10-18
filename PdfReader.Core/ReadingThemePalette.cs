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

    public static ReadingThemePalette DefaultOled { get; } = new(
        ReadingThemeMode.Oled,
        RgbColor.Black,
        RgbColor.DefaultDarkText,
        RgbColor.Black,
        ImageDimAmount: 0.10);

    // Obsidian default dark theme: --color-base-00 (editor), --color-base-100 (text), --color-base-20 (secondary).
    public static ReadingThemePalette DefaultObsidian { get; } = new(
        ReadingThemeMode.Obsidian,
        new RgbColor(0x1C, 0x1C, 0x1C),
        new RgbColor(0xDA, 0xDA, 0xDA),
        new RgbColor(0x28, 0x28, 0x28),
        ImageDimAmount: 0.10);

    // GitHub Primer dark: bgColor-default, fgColor-default, bgColor-inset.
    public static ReadingThemePalette DefaultGitHub { get; } = new(
        ReadingThemeMode.GitHub,
        new RgbColor(0x0D, 0x11, 0x17),
        new RgbColor(0xF0, 0xF6, 0xFC),
        new RgbColor(0x01, 0x04, 0x09),
        ImageDimAmount: 0.10);

    // JetBrains new UI Dark: editor background (Gray1), editor text, tool window background (Gray2).
    public static ReadingThemePalette DefaultJetBrains { get; } = new(
        ReadingThemeMode.JetBrains,
        new RgbColor(0x1E, 0x1F, 0x22),
        new RgbColor(0xBC, 0xBE, 0xC4),
        new RgbColor(0x2B, 0x2D, 0x30),
        ImageDimAmount: 0.10);

    public static ReadingThemePalette GetDefault(ReadingThemeMode mode) => mode switch
    {
        ReadingThemeMode.Original => Original,
        ReadingThemeMode.Oled => DefaultOled,
        ReadingThemeMode.Obsidian => DefaultObsidian,
        ReadingThemeMode.GitHub => DefaultGitHub,
        ReadingThemeMode.JetBrains => DefaultJetBrains,
        _ => DefaultDark,
    };
}
