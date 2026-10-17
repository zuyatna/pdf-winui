using System;

namespace PdfReader.Core;

public readonly record struct LuminanceRemapMatrix
{
    public float M11 { get; init; }
    public float M12 { get; init; }
    public float M13 { get; init; }
    public float M14 { get; init; }

    public float M21 { get; init; }
    public float M22 { get; init; }
    public float M23 { get; init; }
    public float M24 { get; init; }

    public float M31 { get; init; }
    public float M32 { get; init; }
    public float M33 { get; init; }
    public float M34 { get; init; }

    public float M41 { get; init; }
    public float M42 { get; init; }
    public float M43 { get; init; }
    public float M44 { get; init; }

    public float M51 { get; init; }
    public float M52 { get; init; }
    public float M53 { get; init; }
    public float M54 { get; init; }

    public RgbColor Evaluate(RgbColor input)
    {
        float r = input.R / 255f;
        float g = input.G / 255f;
        float b = input.B / 255f;

        float outR = (r * M11) + (g * M21) + (b * M31) + M51;
        float outG = (r * M12) + (g * M22) + (b * M32) + M52;
        float outB = (r * M13) + (g * M23) + (b * M33) + M53;

        return new RgbColor(
            ToByte(outR),
            ToByte(outG),
            ToByte(outB));
    }

    private static byte ToByte(float normalized)
    {
        float clamped = Math.Clamp(normalized * 255f, 0f, 255f);
        return (byte)MathF.Round(clamped, MidpointRounding.AwayFromZero);
    }
}
