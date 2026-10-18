using Microsoft.VisualStudio.TestTools.UnitTesting;
using PdfReader.App.Services;
using PdfReader.Core;

namespace PdfReader.Settings.Tests;

[TestClass]
public sealed class ReadingThemeSettingsTests
{
    [TestMethod]
    [DataRow(ReadingThemeMode.Dark)]
    [DataRow(ReadingThemeMode.Oled)]
    [DataRow(ReadingThemeMode.Obsidian)]
    [DataRow(ReadingThemeMode.GitHub)]
    [DataRow(ReadingThemeMode.JetBrains)]
    public void UncustomizedThemeUsesPresetDefaults(ReadingThemeMode mode)
    {
        var settings = new ReaderSettings { ReadingTheme = mode };

        ReadingThemePalette palette = settings.ToReadingThemePalette();

        Assert.AreEqual(ReadingThemePalette.GetDefault(mode), palette);
    }

    [TestMethod]
    public void SwitchingThemesKeepsCustomizedPalettesIndependent()
    {
        var settings = new ReaderSettings
        {
            ReadingTheme = ReadingThemeMode.Oled,
            CustomPalettes = new Dictionary<ReadingThemeMode, ReadingPaletteHex>
            {
                [ReadingThemeMode.Dark] = new("#121314", "#ECEDEE", "#151617"),
                [ReadingThemeMode.Oled] = new("#010203", "#DADBDC", "#040506"),
                [ReadingThemeMode.GitHub] = new("#0A0B0C", "#E1E2E3", "#020304"),
            },
            ImageDimPercentage = 25,
        };

        ReadingThemePalette oled = settings.ToReadingThemePalette();
        ReadingThemePalette dark = (settings with { ReadingTheme = ReadingThemeMode.Dark }).ToReadingThemePalette();
        ReadingThemePalette gitHub = (settings with { ReadingTheme = ReadingThemeMode.GitHub }).ToReadingThemePalette();
        ReadingThemePalette jetBrains = (settings with { ReadingTheme = ReadingThemeMode.JetBrains }).ToReadingThemePalette();
        ReadingThemePalette original = (settings with { ReadingTheme = ReadingThemeMode.Original }).ToReadingThemePalette();

        Assert.AreEqual(new ReadingThemePalette(ReadingThemeMode.Oled,
            RgbColor.FromHex("#010203"), RgbColor.FromHex("#DADBDC"), RgbColor.FromHex("#040506"), 0.25), oled);
        Assert.AreEqual(RgbColor.FromHex("#121314"), dark.PageBackground);
        Assert.AreEqual(RgbColor.FromHex("#ECEDEE"), dark.Text);
        Assert.AreEqual(RgbColor.FromHex("#151617"), dark.Canvas);
        Assert.AreEqual(ReadingThemeMode.GitHub, gitHub.Mode);
        Assert.AreEqual(RgbColor.FromHex("#0A0B0C"), gitHub.PageBackground);
        Assert.AreEqual(ReadingThemePalette.DefaultJetBrains with { ImageDimAmount = 0.25 }, jetBrains);
        Assert.AreEqual(ReadingThemePalette.Original, original);
    }

    [TestMethod]
    public void InvalidOrMissingColorsFallBackToSelectedThemeDefaults()
    {
        var settings = new ReaderSettings
        {
            ReadingTheme = ReadingThemeMode.Obsidian,
            CustomPalettes = new Dictionary<ReadingThemeMode, ReadingPaletteHex>
            {
                [ReadingThemeMode.Obsidian] = new("invalid", "#12345", null!),
            },
        };

        ReadingThemePalette palette = settings.ToReadingThemePalette();

        Assert.AreEqual(ReadingThemePalette.DefaultObsidian, palette);
        Assert.AreEqual("#282828", settings.GetPaletteHex(ReadingThemeMode.Obsidian).CanvasHex);
    }

    [TestMethod]
    public void GetPaletteHexReturnsPresetHexForUncustomizedTheme()
    {
        var settings = new ReaderSettings();

        ReadingPaletteHex jetBrains = settings.GetPaletteHex(ReadingThemeMode.JetBrains);

        Assert.AreEqual(new ReadingPaletteHex("#1E1F22", "#BCBEC4", "#2B2D30"), jetBrains);
    }

    [TestMethod]
    public async Task SavedPresetThemeReloadsWithCustomPalettes()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "settings.json");
            var service = new ReaderSettingsService(path);
            var settings = new ReaderSettings
            {
                UiTheme = AppUiTheme.Light,
                ReadingTheme = ReadingThemeMode.JetBrains,
                CustomPalettes = new Dictionary<ReadingThemeMode, ReadingPaletteHex>
                {
                    [ReadingThemeMode.JetBrains] = new("#1E1F22", "#E0D0C0", "#2B2D30"),
                    [ReadingThemeMode.Dark] = new("#212223", "#FAF9F5", "#1F1E1D"),
                },
            };

            service.Save(settings);
            await service.FlushAsync();
            ReaderSettings reloaded = service.Load();

            Assert.AreEqual(ReadingThemeMode.JetBrains, reloaded.ReadingTheme);
            Assert.AreEqual(settings.ToReadingThemePalette(), reloaded.ToReadingThemePalette());
            Assert.AreEqual(RgbColor.FromHex("#E0D0C0"), reloaded.ToReadingThemePalette().Text);
            Assert.AreEqual("#212223", reloaded.GetPaletteHex(ReadingThemeMode.Dark).PageBackgroundHex);
            Assert.AreEqual(AppUiTheme.Light, reloaded.UiTheme);
            Assert.DoesNotContain("DarkPageBackgroundHex", await File.ReadAllTextAsync(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task LegacyFlatPaletteFieldsMigrateIntoCustomPalettes()
    {
        string directory = CreateTempDirectory();
        try
        {
            string path = Path.Combine(directory, "settings.json");
            var service = new ReaderSettingsService(path);
            await File.WriteAllTextAsync(path,
                """{"ReadingTheme":2,"DarkPageBackgroundHex":"#303132","OledTextHex":"#E0D0C0"}""");

            ReaderSettings legacy = service.Load();

            Assert.AreEqual(ReadingThemeMode.Oled, legacy.ReadingTheme);
            Assert.AreEqual(new ReadingPaletteHex("#303132", "#FAF9F5", "#1F1E1D"),
                legacy.GetPaletteHex(ReadingThemeMode.Dark));
            Assert.AreEqual(new ReadingPaletteHex("#000000", "#E0D0C0", "#000000"),
                legacy.GetPaletteHex(ReadingThemeMode.Oled));
            Assert.AreEqual(ReadingPaletteHex.From(ReadingThemePalette.DefaultGitHub),
                legacy.GetPaletteHex(ReadingThemeMode.GitHub));
            Assert.IsNull(legacy.DarkPageBackgroundHex);
            Assert.IsNull(legacy.OledTextHex);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void LegacyMigrationKeepsExistingCustomPaletteEntries()
    {
        var settings = new ReaderSettings
        {
            DarkPageBackgroundHex = "#303132",
            CustomPalettes = new Dictionary<ReadingThemeMode, ReadingPaletteHex>
            {
                [ReadingThemeMode.Dark] = new("#404142", "#FAF9F5", "#1F1E1D"),
            },
        };

        ReaderSettings migrated = settings.WithLegacyPalettesMigrated();

        Assert.AreEqual("#404142", migrated.GetPaletteHex(ReadingThemeMode.Dark).PageBackgroundHex);
        Assert.IsFalse(migrated.CustomPalettes.ContainsKey(ReadingThemeMode.Oled));
        Assert.IsNull(migrated.DarkPageBackgroundHex);
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"pdf-reader-theme-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
