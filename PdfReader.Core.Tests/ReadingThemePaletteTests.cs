using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PdfReader.Core.Tests;

[TestClass]
public sealed class ReadingThemePaletteTests
{
    [TestMethod]
    [DataRow(ReadingThemeMode.Dark, "#262624", "#FAF9F5", "#1F1E1D")]
    [DataRow(ReadingThemeMode.Oled, "#000000", "#FAF9F5", "#000000")]
    [DataRow(ReadingThemeMode.Obsidian, "#1C1C1C", "#DADADA", "#282828")]
    [DataRow(ReadingThemeMode.GitHub, "#0D1117", "#F0F6FC", "#010409")]
    [DataRow(ReadingThemeMode.JetBrains, "#1E1F22", "#BCBEC4", "#2B2D30")]
    public void GetDefault_ReturnsPresetColorsForDarkMode(
        ReadingThemeMode mode, string pageBackground, string text, string canvas)
    {
        // Act
        ReadingThemePalette palette = ReadingThemePalette.GetDefault(mode);

        // Assert
        Assert.AreEqual(mode, palette.Mode);
        Assert.IsTrue(palette.IsRemapEnabled);
        Assert.AreEqual(pageBackground, palette.PageBackground.ToHex());
        Assert.AreEqual(text, palette.Text.ToHex());
        Assert.AreEqual(canvas, palette.Canvas.ToHex());
        Assert.AreEqual(0.10, palette.ImageDimAmount, 0.0001);
    }

    [TestMethod]
    public void GetDefault_ReturnsOriginalForOriginalAndDarkForUnknownMode()
    {
        // Act
        ReadingThemePalette original = ReadingThemePalette.GetDefault(ReadingThemeMode.Original);
        ReadingThemePalette unknown = ReadingThemePalette.GetDefault((ReadingThemeMode)99);

        // Assert
        Assert.AreEqual(ReadingThemePalette.Original, original);
        Assert.IsFalse(original.IsRemapEnabled);
        Assert.AreEqual(ReadingThemePalette.DefaultDark, unknown);
    }

    [TestMethod]
    [DataRow(ReadingThemeMode.Obsidian)]
    [DataRow(ReadingThemeMode.GitHub)]
    [DataRow(ReadingThemeMode.JetBrains)]
    public void PresetRemap_MapsPaperToPageBackgroundAndInkToText(ReadingThemeMode mode)
    {
        // Arrange
        ReadingThemePalette palette = ReadingThemePalette.GetDefault(mode);

        // Act
        LuminanceRemapMatrix matrix = LuminanceRemapCalculator.CreateRemap(palette.PageBackground, palette.Text);

        // Assert
        Assert.AreEqual(palette.PageBackground, matrix.Evaluate(RgbColor.White));
        Assert.AreEqual(palette.Text, matrix.Evaluate(RgbColor.Black));
    }
}
