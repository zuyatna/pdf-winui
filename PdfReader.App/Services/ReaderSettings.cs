using System;
using System.Collections.Generic;
using System.IO;
using PdfReader.Core;

namespace PdfReader.App.Services;

public sealed record ReaderSettings
{
    public const string DefaultDarkPageBackgroundHex = "#262624";
    public const string DefaultDarkTextHex = "#FAF9F5";
    public const string DefaultDarkCanvasHex = "#1F1E1D";
    public const string DefaultOledPageBackgroundHex = "#000000";
    public const string DefaultOledTextHex = "#FAF9F5";
    public const string DefaultOledCanvasHex = "#000000";
    public const double DefaultImageDimPercentage = 10.0;
    public const int MaxRecentDocuments = 10;

    public AppUiTheme UiTheme { get; init; } = AppUiTheme.System;

    public ReadingThemeMode ReadingTheme { get; init; } = ReadingThemeMode.Dark;

    public string DarkPageBackgroundHex { get; init; } = DefaultDarkPageBackgroundHex;

    public string DarkTextHex { get; init; } = DefaultDarkTextHex;

    public string DarkCanvasHex { get; init; } = DefaultDarkCanvasHex;

    public string OledPageBackgroundHex { get; init; } = DefaultOledPageBackgroundHex;

    public string OledTextHex { get; init; } = DefaultOledTextHex;

    public string OledCanvasHex { get; init; } = DefaultOledCanvasHex;

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

        bool isOled = ReadingTheme == ReadingThemeMode.Oled;
        ReadingThemePalette defaults = isOled ? ReadingThemePalette.DefaultOled : ReadingThemePalette.DefaultDark;
        RgbColor pageBg = RgbColor.TryParseHex(isOled ? OledPageBackgroundHex : DarkPageBackgroundHex, out RgbColor parsedBg)
            ? parsedBg
            : defaults.PageBackground;
        RgbColor text = RgbColor.TryParseHex(isOled ? OledTextHex : DarkTextHex, out RgbColor parsedText)
            ? parsedText
            : defaults.Text;
        RgbColor canvas = RgbColor.TryParseHex(isOled ? OledCanvasHex : DarkCanvasHex, out RgbColor parsedCanvas)
            ? parsedCanvas
            : defaults.Canvas;

        double dimAmount = Math.Clamp(ImageDimPercentage, 0.0, 50.0) / 100.0;
        return new ReadingThemePalette(defaults.Mode, pageBg, text, canvas, dimAmount);
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
