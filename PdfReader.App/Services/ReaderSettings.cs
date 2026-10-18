using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using PdfReader.Core;

namespace PdfReader.App.Services;

public sealed record ReaderSettings
{
    public const double DefaultImageDimPercentage = 10.0;
    public const int MaxRecentDocuments = 10;

    public static IReadOnlyDictionary<ReadingThemeMode, ReadingPaletteHex> EmptyPalettes { get; } =
        new Dictionary<ReadingThemeMode, ReadingPaletteHex>();

    public AppUiTheme UiTheme { get; init; } = AppUiTheme.System;

    public ReadingThemeMode ReadingTheme { get; init; } = ReadingThemeMode.Dark;

    // User-edited colors per reading theme. Themes without an entry use their preset defaults.
    public IReadOnlyDictionary<ReadingThemeMode, ReadingPaletteHex> CustomPalettes { get; init; } = EmptyPalettes;

    // Legacy flat palette fields written by earlier versions. Read once, migrated into CustomPalettes
    // by WithLegacyPalettesMigrated, and no longer written.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DarkPageBackgroundHex { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DarkTextHex { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DarkCanvasHex { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OledPageBackgroundHex { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OledTextHex { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OledCanvasHex { get; init; }

    public double ImageDimPercentage { get; init; } = DefaultImageDimPercentage;

    public IReadOnlyList<DocumentViewState> RecentDocuments { get; init; } = [];

    // Kept independently so clearing or evicting recent files never deletes bookmarks.
    public IReadOnlyList<DocumentViewState> BookmarkedDocuments { get; init; } = [];

    public ReadingThemePalette ToReadingThemePalette()
    {
        if (ReadingTheme == ReadingThemeMode.Original)
        {
            return ReadingThemePalette.Original;
        }

        ReadingThemePalette defaults = ReadingThemePalette.GetDefault(ReadingTheme);
        ReadingPaletteHex hex = GetPaletteHex(defaults.Mode);
        RgbColor pageBg = RgbColor.TryParseHex(hex.PageBackgroundHex, out RgbColor parsedBg)
            ? parsedBg
            : defaults.PageBackground;
        RgbColor text = RgbColor.TryParseHex(hex.TextHex, out RgbColor parsedText)
            ? parsedText
            : defaults.Text;
        RgbColor canvas = RgbColor.TryParseHex(hex.CanvasHex, out RgbColor parsedCanvas)
            ? parsedCanvas
            : defaults.Canvas;

        double dimAmount = Math.Clamp(ImageDimPercentage, 0.0, 50.0) / 100.0;
        return new ReadingThemePalette(defaults.Mode, pageBg, text, canvas, dimAmount);
    }

    public ReadingPaletteHex GetPaletteHex(ReadingThemeMode mode) => ResolvePaletteHex(CustomPalettes, mode);

    // Returns the stored colors for a theme, filling missing entries or fields from the preset.
    public static ReadingPaletteHex ResolvePaletteHex(
        IReadOnlyDictionary<ReadingThemeMode, ReadingPaletteHex>? palettes,
        ReadingThemeMode mode)
    {
        ReadingPaletteHex defaults = ReadingPaletteHex.From(ReadingThemePalette.GetDefault(mode));
        if (palettes is null || !palettes.TryGetValue(mode, out ReadingPaletteHex? custom) || custom is null)
        {
            return defaults;
        }

        return new ReadingPaletteHex(
            custom.PageBackgroundHex ?? defaults.PageBackgroundHex,
            custom.TextHex ?? defaults.TextHex,
            custom.CanvasHex ?? defaults.CanvasHex);
    }

    public ReaderSettings WithLegacyPalettesMigrated()
    {
        var palettes = new Dictionary<ReadingThemeMode, ReadingPaletteHex>(CustomPalettes ?? EmptyPalettes);
        AddLegacyPalette(palettes, ReadingThemeMode.Dark, DarkPageBackgroundHex, DarkTextHex, DarkCanvasHex);
        AddLegacyPalette(palettes, ReadingThemeMode.Oled, OledPageBackgroundHex, OledTextHex, OledCanvasHex);

        return this with
        {
            CustomPalettes = palettes.AsReadOnly(),
            DarkPageBackgroundHex = null,
            DarkTextHex = null,
            DarkCanvasHex = null,
            OledPageBackgroundHex = null,
            OledTextHex = null,
            OledCanvasHex = null,
        };
    }

    private static void AddLegacyPalette(
        Dictionary<ReadingThemeMode, ReadingPaletteHex> palettes,
        ReadingThemeMode mode,
        string? pageBackgroundHex,
        string? textHex,
        string? canvasHex)
    {
        if (palettes.ContainsKey(mode) || (pageBackgroundHex is null && textHex is null && canvasHex is null))
        {
            return;
        }

        ReadingPaletteHex defaults = ReadingPaletteHex.From(ReadingThemePalette.GetDefault(mode));
        palettes[mode] = new ReadingPaletteHex(
            pageBackgroundHex ?? defaults.PageBackgroundHex,
            textHex ?? defaults.TextHex,
            canvasHex ?? defaults.CanvasHex);
    }

    public DocumentViewState? FindDocumentState(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        string normalized = NormalizePath(filePath);
        foreach (DocumentViewState candidate in RecentDocuments ?? [])
        {
            if (string.Equals(NormalizePath(candidate.FilePath), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        foreach (DocumentViewState candidate in BookmarkedDocuments ?? [])
        {
            if (string.Equals(NormalizePath(candidate.FilePath), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    public ReaderSettings WithUpdatedDocumentState(DocumentViewState state, int maxEntries = MaxRecentDocuments)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntries, 1);

        string normalized = NormalizePath(state.FilePath);
        var updated = new List<DocumentViewState>(maxEntries)
        {
            state with { FilePath = normalized },
        };

        if (RecentDocuments is not null)
        {
            for (int i = 0; i < RecentDocuments.Count && updated.Count < maxEntries; i++)
            {
                DocumentViewState existing = RecentDocuments[i];
                if (!string.Equals(NormalizePath(existing.FilePath), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    updated.Add(existing);
                }
            }
        }

        var bookmarked = new List<DocumentViewState>();
        foreach (DocumentViewState existing in BookmarkedDocuments ?? [])
        {
            if (!string.Equals(NormalizePath(existing.FilePath), normalized, StringComparison.OrdinalIgnoreCase))
            {
                bookmarked.Add(existing);
            }
        }

        if (state.Bookmarks is { Count: > 0 })
        {
            bookmarked.Add(state with { FilePath = normalized });
        }

        return this with { RecentDocuments = updated.AsReadOnly(), BookmarkedDocuments = bookmarked.AsReadOnly() };
    }

    private static string NormalizePath(string filePath)
    {
        try
        {
            return Path.GetFullPath(filePath);
        }
        catch
        {
            return filePath;
        }
    }
}
