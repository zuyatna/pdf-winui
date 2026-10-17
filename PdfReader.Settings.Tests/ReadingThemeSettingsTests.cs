using Microsoft.VisualStudio.TestTools.UnitTesting;
using PdfReader.App.Services;
using PdfReader.Core;

namespace PdfReader.Settings.Tests;

[TestClass]
public sealed class ReadingThemeSettingsTests
{
    [TestMethod]
    public void OledDefaultsUseBlackPaperAndCanvasWithWarmText()
    {
        var settings = new ReaderSettings { ReadingTheme = ReadingThemeMode.Oled };

        ReadingThemePalette palette = settings.ToReadingThemePalette();

        Assert.AreEqual(ReadingThemeMode.Oled, palette.Mode);
        Assert.IsTrue(palette.IsRemapEnabled);
        Assert.AreEqual(RgbColor.Black, palette.PageBackground);
        Assert.AreEqual(RgbColor.Black, palette.Canvas);
        Assert.AreEqual(RgbColor.FromHex("#FAF9F5"), palette.Text);
        Assert.AreEqual(0.10, palette.ImageDimAmount, 0.0001);
    }

    [TestMethod]
    public void SwitchingThemesKeepsCustomizedPalettesIndependent()
    {
        var settings = new ReaderSettings
        {
            ReadingTheme = ReadingThemeMode.Oled,
            DarkPageBackgroundHex = "#121314",
            DarkTextHex = "#ECEDEE",
            DarkCanvasHex = "#151617",
            OledPageBackgroundHex = "#010203",
            OledTextHex = "#DADBDC",
            OledCanvasHex = "#040506",
            ImageDimPercentage = 25,
        };

        ReadingThemePalette oled = settings.ToReadingThemePalette();
        ReadingThemePalette dark = (settings with { ReadingTheme = ReadingThemeMode.Dark }).ToReadingThemePalette();
        ReadingThemePalette original = (settings with { ReadingTheme = ReadingThemeMode.Original }).ToReadingThemePalette();

        Assert.AreEqual(RgbColor.FromHex("#010203"), oled.PageBackground);
        Assert.AreEqual(RgbColor.FromHex("#DADBDC"), oled.Text);
        Assert.AreEqual(RgbColor.FromHex("#040506"), oled.Canvas);
        Assert.AreEqual(0.25, oled.ImageDimAmount, 0.0001);
        Assert.AreEqual(RgbColor.FromHex("#121314"), dark.PageBackground);
        Assert.AreEqual(RgbColor.FromHex("#ECEDEE"), dark.Text);
        Assert.AreEqual(RgbColor.FromHex("#151617"), dark.Canvas);
        Assert.AreEqual(ReadingThemePalette.Original, original);
    }

    [TestMethod]
    public void InvalidOledColorsFallBackToOledDefaults()
    {
        var settings = new ReaderSettings
        {
            ReadingTheme = ReadingThemeMode.Oled,
            OledPageBackgroundHex = "invalid",
            OledTextHex = "#12345",
            OledCanvasHex = "",
        };

        ReadingThemePalette palette = settings.ToReadingThemePalette();

        Assert.AreEqual(RgbColor.Black, palette.PageBackground);
        Assert.AreEqual(RgbColor.DefaultDarkText, palette.Text);
        Assert.AreEqual(RgbColor.Black, palette.Canvas);
    }

    [TestMethod]
    public async Task SavedOledThemeReloadsAndLegacySettingsRetainWarmPalette()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"pdf-reader-oled-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "settings.json");
            var service = new ReaderSettingsService(path);
            var settings = new ReaderSettings
            {
                UiTheme = AppUiTheme.Light,
                ReadingTheme = ReadingThemeMode.Oled,
                OledTextHex = "#E0D0C0",
                DarkPageBackgroundHex = "#212223",
            };

            service.Save(settings);
            await service.FlushAsync();
            ReaderSettings reloaded = service.Load();

            Assert.AreEqual(ReadingThemeMode.Oled, reloaded.ReadingTheme);
            Assert.AreEqual(settings.ToReadingThemePalette(), reloaded.ToReadingThemePalette());
            Assert.AreEqual(settings.DarkPageBackgroundHex, reloaded.DarkPageBackgroundHex);
            Assert.AreEqual(RgbColor.FromHex("#E0D0C0"), reloaded.ToReadingThemePalette().Text);
            Assert.AreEqual(AppUiTheme.Light, reloaded.UiTheme);

            await File.WriteAllTextAsync(path, """{"ReadingTheme":1,"DarkPageBackgroundHex":"#303132"}""");
            ReaderSettings legacy = service.Load();

            Assert.AreEqual(ReadingThemeMode.Dark, legacy.ReadingTheme);
            Assert.AreEqual(RgbColor.FromHex("#303132"), legacy.ToReadingThemePalette().PageBackground);
            Assert.AreEqual(RgbColor.Black,
                (legacy with { ReadingTheme = ReadingThemeMode.Oled }).ToReadingThemePalette().PageBackground);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
