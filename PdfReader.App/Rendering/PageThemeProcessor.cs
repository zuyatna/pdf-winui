using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using PdfReader.Core;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Windows.UI;

namespace PdfReader.App.Rendering;

public static class PageThemeProcessor
{
    private static readonly object s_deviceGate = new();
    private static long s_initialDeviceCacheBytes = -1;
    private static long s_deviceCacheBytes = -1;

    internal static long InitialDeviceCacheBytes => Interlocked.Read(ref s_initialDeviceCacheBytes);

    internal static long DeviceCacheBytes => Interlocked.Read(ref s_deviceCacheBytes);

    public static Matrix5x4 CreateRemap(Color background, Color text)
    {
        float tr = text.R / 255f;
        float tg = text.G / 255f;
        float tb = text.B / 255f;
        float dr = (background.R / 255f) - tr;
        float dg = (background.G / 255f) - tg;
        float db = (background.B / 255f) - tb;

        return new Matrix5x4
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

    public static byte[] ApplyTheme(RenderedPageBuffer rendered, ReadingThemePalette palette)
    {
        ArgumentNullException.ThrowIfNull(rendered);
        ArgumentNullException.ThrowIfNull(palette);

        if (!palette.IsRemapEnabled)
        {
            return rendered.BgraPixels;
        }

        Color background = ToUiColor(palette.PageBackground);
        Color text = ToUiColor(palette.Text);
        Matrix5x4 remapMatrix = CreateRemap(background, text);

        int width = rendered.Size.Width;
        int height = rendered.Size.Height;
        bool isScanned = rendered.IsScannedPage;
        IReadOnlyList<PixelRect> imageBounds = rendered.EffectiveImageBounds;
        double dimAmount = palette.ClampedImageDimAmount;

        try
        {
            lock (s_deviceGate)
            {
                CanvasDevice device = CanvasDevice.GetSharedDevice();
                long cacheBytes = (long)Math.Min(device.MaximumCacheSize, (ulong)long.MaxValue);
                Interlocked.CompareExchange(ref s_initialDeviceCacheBytes, cacheBytes, -1);
                Interlocked.Exchange(ref s_deviceCacheBytes, cacheBytes);

                using var sourceBitmap = CanvasBitmap.CreateFromBytes(
                    device,
                    rendered.BgraPixels,
                    width,
                    height,
                    DirectXPixelFormat.B8G8R8A8UIntNormalized);

                using var renderTarget = new CanvasRenderTarget(device, width, height, 96f);
                using var remapEffect = new ColorMatrixEffect
                {
                    Source = sourceBitmap,
                    ColorMatrix = remapMatrix,
                    ClampOutput = true,
                };

                using (CanvasDrawingSession ds = renderTarget.CreateDrawingSession())
                {
                    ds.Clear(background);
                    ds.DrawImage(remapEffect);

                    if (!isScanned && imageBounds.Count > 0)
                    {
                        byte dimAlpha = (byte)Math.Round(dimAmount * 255.0, MidpointRounding.AwayFromZero);
                        Color dimOverlay = Color.FromArgb(dimAlpha, 0, 0, 0);

                        for (int i = 0; i < imageBounds.Count; i++)
                        {
                            PixelRect rect = imageBounds[i].Clamp(rendered.Size);
                            if (rect.IsEmpty)
                            {
                                continue;
                            }

                            var bounds = new Rect(rect.X, rect.Y, rect.Width, rect.Height);
                            ds.DrawImage(sourceBitmap, bounds, bounds);

                            if (dimAlpha > 0)
                            {
                                ds.FillRectangle(bounds, dimOverlay);
                            }
                        }
                    }
                }

                return renderTarget.GetPixelBytes();
            }
        }
        catch (Exception)
        {
            return ApplyThemeSoftwareFallback(rendered, palette, isScanned, imageBounds, dimAmount);
        }
    }

    public static Color ToUiColor(RgbColor color) =>
        Color.FromArgb(255, color.R, color.G, color.B);

    private static byte[] ApplyThemeSoftwareFallback(
        RenderedPageBuffer rendered,
        ReadingThemePalette palette,
        bool isScanned,
        IReadOnlyList<PixelRect> imageBounds,
        double dimAmount)
    {
        LuminanceRemapMatrix matrix = LuminanceRemapCalculator.CreateRemap(palette.PageBackground, palette.Text);
        byte[] src = rendered.BgraPixels;
        var dst = new byte[src.Length];

        for (int i = 0; i < src.Length; i += 4)
        {
            var color = new RgbColor(src[i + 2], src[i + 1], src[i]);
            RgbColor mapped = matrix.Evaluate(color);
            dst[i] = mapped.B;
            dst[i + 1] = mapped.G;
            dst[i + 2] = mapped.R;
            dst[i + 3] = src[i + 3];
        }

        if (!isScanned && imageBounds.Count > 0)
        {
            double keepFactor = Math.Clamp(1.0 - dimAmount, 0.0, 1.0);
            int stride = rendered.Stride;

            for (int r = 0; r < imageBounds.Count; r++)
            {
                PixelRect rect = imageBounds[r].Clamp(rendered.Size);
                if (rect.IsEmpty)
                {
                    continue;
                }

                for (int y = rect.Y; y < rect.Y + rect.Height; y++)
                {
                    int rowOffset = y * stride;
                    for (int x = rect.X; x < rect.X + rect.Width; x++)
                    {
                        int idx = rowOffset + (x * 4);
                        dst[idx] = (byte)Math.Round(src[idx] * keepFactor, MidpointRounding.AwayFromZero);
                        dst[idx + 1] = (byte)Math.Round(src[idx + 1] * keepFactor, MidpointRounding.AwayFromZero);
                        dst[idx + 2] = (byte)Math.Round(src[idx + 2] * keepFactor, MidpointRounding.AwayFromZero);
                        dst[idx + 3] = src[idx + 3];
                    }
                }
            }
        }

        return dst;
    }
}
