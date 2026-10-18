using PdfReader.Core;

namespace PdfReader.App.Services;

// Persisted, user-editable form of a reading palette. Values may be invalid or null after editing
// or deserialization; ReaderSettings falls back to the theme's preset color in that case.
public sealed record ReadingPaletteHex(string PageBackgroundHex, string TextHex, string CanvasHex)
{
    public static ReadingPaletteHex From(ReadingThemePalette palette) => new(
        palette.PageBackground.ToHex(),
        palette.Text.ToHex(),
        palette.Canvas.ToHex());
}
