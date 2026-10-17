using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PdfReader.Core.Tests;

[TestClass]
public sealed class PixelSizeTests
{
    [TestMethod]
    public void FromPageSize_WithStandardLetterPageAndScale_ComputesExpectedPixelDimensions()
    {
        // Arrange: US Letter is 612 x 792 points (8.5" x 11" at 72 pt/in)
        // At 96 DPI and 1.5x rasterization scale: 612 * (96/72) * 1.5 = 1224, 792 * (96/72) * 1.5 = 1584
        var pageSize = new PdfPageSize(612, 792);

        // Act
        PixelSize pixelSize = PixelSize.FromPageSize(pageSize, scale: 1.5);

        // Assert
        Assert.AreEqual(1224, pixelSize.Width);
        Assert.AreEqual(1584, pixelSize.Height);
        Assert.AreEqual(1224 * 1584 * 4, pixelSize.ByteLength);
    }

    [TestMethod]
    public void FromPageSize_WithTinyDimensions_ClampsToMinimumOnePixel()
    {
        // Arrange
        var pageSize = new PdfPageSize(0.1, 0.1);

        // Act
        PixelSize pixelSize = PixelSize.FromPageSize(pageSize, scale: 0.01);

        // Assert
        Assert.AreEqual(1, pixelSize.Width);
        Assert.AreEqual(1, pixelSize.Height);
    }

    [TestMethod]
    public void FromPageSize_WithInvalidArguments_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var validPage = new PdfPageSize(612, 792);

        // Act & Assert
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PixelSize.FromPageSize(new PdfPageSize(0, 792), scale: 1.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PixelSize.FromPageSize(new PdfPageSize(612, -10), scale: 1.0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PixelSize.FromPageSize(validPage, scale: 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            PixelSize.FromPageSize(validPage, scale: double.NaN));
    }
}
