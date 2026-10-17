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
    public const double DefaultImageDimPercentage = 10.0;
    public const int MaxRecentDocuments = 10;

    public AppUiTheme UiTheme { get; init; } = AppUiTheme.System;

    public ReadingThemeMode ReadingTheme { get; init; } = ReadingThemeMode.Dark;

    public string DarkPageBackgroundHex { get; init; } = DefaultDarkPageBackgroundHex;

    public string DarkTextHex { get; init; } = DefaultDarkTextHex;

    public string DarkCanvasHex { get; init; } = DefaultDarkCanvasHex;

    public double ImageDimPercentage { get; init; } = DefaultImageDimPercentage;

    public IReadOnlyList<DocumentViewState> RecentDocuments { get; init; } = [];

    public ReadingThemePalette ToReadingThemePalette()
    {
        if (ReadingTheme == ReadingThemeMode.Original)
        {
            return ReadingThemePalette.Original;
        }

        RgbColor pageBg = RgbColor.TryParseHex(DarkPageBackgroundHex, out RgbColor parsedBg)
            ? parsedBg
            : RgbColor.DefaultDarkPageBackground;
        RgbColor text = RgbColor.TryParseHex(DarkTextHex, out RgbColor parsedText)
            ? parsedText
            : RgbColor.DefaultDarkText;
        RgbColor canvas = RgbColor.TryParseHex(DarkCanvasHex, out RgbColor parsedCanvas)
            ? parsedCanvas
            : RgbColor.DefaultDarkCanvas;

        double dimAmount = Math.Clamp(ImageDimPercentage, 0.0, 50.0) / 100.0;
        return new ReadingThemePalette(ReadingThemeMode.Dark, pageBg, text, canvas, dimAmount);
    }

    public DocumentViewState? FindDocumentState(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || RecentDocuments is null || RecentDocuments.Count == 0)
        {
            return null;
        }

        string normalized = NormalizePath(filePath);
        for (int i = 0; i < RecentDocuments.Count; i++)
        {
            DocumentViewState candidate = RecentDocuments[i];
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

        return this with { RecentDocuments = updated.AsReadOnly() };
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
