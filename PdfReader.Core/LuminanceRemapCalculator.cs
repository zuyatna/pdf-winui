namespace PdfReader.Core;

public static class LuminanceRemapCalculator
{
    public static LuminanceRemapMatrix CreateRemap(RgbColor background, RgbColor text)
    {
        float tr = text.R / 255f;
        float tg = text.G / 255f;
        float tb = text.B / 255f;
        float dr = (background.R / 255f) - tr;
        float dg = (background.G / 255f) - tg;
        float db = (background.B / 255f) - tb;

        return new LuminanceRemapMatrix
        {
            M11 = .299f * dr,
            M12 = .299f * dg,
            M13 = .299f * db,
            M21 = .587f * dr,
            M22 = .587f * dg,
            M23 = .587f * db,
            M31 = .114f * dr,
            M32 = .114f * dg,
            M33 = .114f * db,
            M44 = 1f,
            M51 = tr,
            M52 = tg,
            M53 = tb,
        };
    }
}
