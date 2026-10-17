using System;
using System.Globalization;

namespace PdfReader.Core;

public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public static RgbColor DefaultDarkPageBackground => new(0x26, 0x26, 0x24);

    public static RgbColor DefaultDarkText => new(0xFA, 0xF9, 0xF5);

    public static RgbColor DefaultDarkCanvas => new(0x1F, 0x1E, 0x1D);

    public static RgbColor White => new(0xFF, 0xFF, 0xFF);

    public static RgbColor Black => new(0x00, 0x00, 0x00);

    public static RgbColor FromHex(string hex)
    {
        if (!TryParseHex(hex, out RgbColor color))
        {
            throw new FormatException($"Invalid hex color '{hex}'. Expected #RRGGBB.");
        }

        return color;
    }

    public static bool TryParseHex(string? hex, out RgbColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        ReadOnlySpan<char> span = hex.AsSpan().Trim();
        if (span.StartsWith("#", StringComparison.Ordinal))
        {
            span = span[1..];
        }

        if (span.Length == 8)
        {
            // Support #AARRGGBB by skipping alpha channel
            span = span[2..];
        }

        if (span.Length != 6)
        {
            return false;
        }

        if (!byte.TryParse(span[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) ||
            !byte.TryParse(span[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) ||
            !byte.TryParse(span[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
        {
            return false;
        }

        color = new RgbColor(r, g, b);
        return true;
    }

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";
}
