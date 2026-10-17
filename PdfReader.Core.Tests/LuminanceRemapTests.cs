using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PdfReader.Core.Tests;

[TestClass]
public sealed class LuminanceRemapTests
{
    [TestMethod]
    public void OledRemapMapsPaperToPureBlackAndInkToWarmText()
    {
        ReadingThemePalette palette = ReadingThemePalette.DefaultOled;

        LuminanceRemapMatrix matrix = LuminanceRemapCalculator.CreateRemap(palette.PageBackground, palette.Text);

        Assert.AreEqual(RgbColor.Black, matrix.Evaluate(RgbColor.White));
        Assert.AreEqual(RgbColor.DefaultDarkText, matrix.Evaluate(RgbColor.Black));
        Assert.AreEqual(new RgbColor(125, 124, 122), matrix.Evaluate(new RgbColor(128, 128, 128)));
    }

    [TestMethod]
    public void CreateRemap_MapsBlackToTextColor_AndWhiteToPageBackground()
    {
        // Arrange: Claude dark reading defaults (#262624 background, #FAF9F5 text)
        RgbColor background = RgbColor.DefaultDarkPageBackground;
        RgbColor text = RgbColor.DefaultDarkText;

        // Act
        LuminanceRemapMatrix matrix = LuminanceRemapCalculator.CreateRemap(background, text);
        RgbColor mappedBlack = matrix.Evaluate(RgbColor.Black);
        RgbColor mappedWhite = matrix.Evaluate(RgbColor.White);

        // Assert
        Assert.AreEqual(text, mappedBlack);
        Assert.AreEqual(background, mappedWhite);
        Assert.AreEqual(1f, matrix.M44, 0.0001f);
    }

    [TestMethod]
    public void CreateRemap_WithMidGray_InterpolatesSmoothlyBetweenTextAndBackground()
    {
        // Arrange
        var background = new RgbColor(20, 40, 60);
        var text = new RgbColor(220, 200, 180);
        var midGray = new RgbColor(128, 128, 128);
        float l = 128f / 255f;

        // Act
        LuminanceRemapMatrix matrix = LuminanceRemapCalculator.CreateRemap(background, text);
        RgbColor mapped = matrix.Evaluate(midGray);

        // Assert: out = text + (background - text) * L
        int expectedR = (int)Math.Round(text.R + ((background.R - text.R) * l), MidpointRounding.AwayFromZero);
        int expectedG = (int)Math.Round(text.G + ((background.G - text.G) * l), MidpointRounding.AwayFromZero);
        int expectedB = (int)Math.Round(text.B + ((background.B - text.B) * l), MidpointRounding.AwayFromZero);

        Assert.AreEqual(expectedR, mapped.R, 1);
        Assert.AreEqual(expectedG, mapped.G, 1);
        Assert.AreEqual(expectedB, mapped.B, 1);
    }

    [TestMethod]
    public void RgbColor_FromHexAndToHex_RoundTripsDefaultDarkPalette()
    {
        // Arrange & Act
        RgbColor bg = RgbColor.FromHex("#262624");
        RgbColor txt = RgbColor.FromHex("#FAF9F5");
        RgbColor canvas = RgbColor.FromHex("#1F1E1D");

        // Assert
        Assert.AreEqual(RgbColor.DefaultDarkPageBackground, bg);
        Assert.AreEqual(RgbColor.DefaultDarkText, txt);
        Assert.AreEqual(RgbColor.DefaultDarkCanvas, canvas);
        Assert.AreEqual("#262624", bg.ToHex());
        Assert.AreEqual("#FAF9F5", txt.ToHex());
        Assert.AreEqual("#1F1E1D", canvas.ToHex());
    }
}
