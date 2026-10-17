using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PdfReader.Core.Tests;

[TestClass]
public sealed class PdfDocumentServiceTests
{
    private static string GetSamplePdfPath()
    {
        string outputSamplePath = Path.Combine(AppContext.BaseDirectory, "samples", "text.pdf");
        if (File.Exists(outputSamplePath))
        {
            return outputSamplePath;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "samples", "text.pdf"));
    }

    [TestMethod]
    public async Task OpenDocumentAsync_WithValidSamplePdf_ReturnsMetadataAndPageSize()
    {
        // Arrange
        await using var service = new PdfDocumentService();
        string samplePath = GetSamplePdfPath();

        // Act
        PdfDocumentMetadata metadata = await service.OpenDocumentAsync(samplePath);

        // Assert
        Assert.IsNotNull(service.CurrentDocument);
        Assert.AreEqual(1, metadata.PageCount);
        Assert.AreEqual(1, metadata.PageSizes.Count);
        Assert.AreEqual(612.0, metadata.PageSizes[0].WidthPoints, 0.01);
        Assert.AreEqual(792.0, metadata.PageSizes[0].HeightPoints, 0.01);

        await service.CloseDocumentAsync();
        Assert.IsNull(service.CurrentDocument);
    }

    [TestMethod]
    public async Task RenderPageAsync_WithValidSamplePdf_ProducesNonWhiteBgraBuffer()
    {
        // Arrange
        await using var service = new PdfDocumentService();
        string samplePath = GetSamplePdfPath();
        PdfDocumentMetadata metadata = await service.OpenDocumentAsync(samplePath);
        PixelSize targetSize = PixelSize.FromPageSize(metadata.PageSizes[0], scale: 0.5);

        // Act
        RenderedPageBuffer buffer = await service.RenderPageAsync(0, targetSize);

        // Assert
        Assert.AreEqual(0, buffer.PageIndex);
        Assert.AreEqual(targetSize, buffer.Size);
        Assert.AreEqual(targetSize.Width * 4, buffer.Stride);
        Assert.AreEqual(targetSize.ByteLength, buffer.BgraPixels.Length);

        bool hasWhitePixel = false;
        bool hasDarkPixel = false;
        byte[] pixels = buffer.BgraPixels;

        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte b = pixels[i];
            byte g = pixels[i + 1];
            byte r = pixels[i + 2];
            byte a = pixels[i + 3];

            if (b == 255 && g == 255 && r == 255 && a == 255)
            {
                hasWhitePixel = true;
            }
            else if (b < 128 && g < 128 && r < 128 && a == 255)
            {
                hasDarkPixel = true;
            }

            if (hasWhitePixel && hasDarkPixel)
            {
                break;
            }
        }

        Assert.IsTrue(hasWhitePixel, "Rendered page buffer should contain white background pixels.");
        Assert.IsTrue(hasDarkPixel, "Rendered page buffer should contain dark foreground pixels.");
    }
}
