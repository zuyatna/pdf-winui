using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PdfReader.Core.Tests;

[TestClass]
public sealed class ScannedPageDetectorTests
{
    [TestMethod]
    public void IsScannedPage_WhenImageCoversAtLeast85Percent_ReturnsTrue()
    {
        // Arrange: 1000x1000 page = 1,000,000 px; 1000x850 image = 850,000 px (exactly 85%)
        var pageSize = new PixelSize(1000, 1000);
        PixelRect[] images = [new PixelRect(0, 0, 1000, 850)];

        // Act
        double coverage = ScannedPageDetector.CalculateImageCoverage(pageSize, images);
        bool isScanned = ScannedPageDetector.IsScannedPage(pageSize, images);

        // Assert
        Assert.AreEqual(0.85, coverage, 0.0001);
        Assert.IsTrue(isScanned);
    }

    [TestMethod]
    public void IsScannedPage_WhenImageCoversBelow85Percent_ReturnsFalse()
    {
        // Arrange: 1000x1000 page; 1000x849 image = 84.9%
        var pageSize = new PixelSize(1000, 1000);
        PixelRect[] images = [new PixelRect(0, 0, 1000, 849)];

        // Act
        bool isScanned = ScannedPageDetector.IsScannedPage(pageSize, images);

        // Assert
        Assert.IsFalse(isScanned);
    }

    [TestMethod]
    public void CalculateImageCoverage_ClampsOutOfBoundsImageRectanglesToPageBounds()
    {
        // Arrange: image extends beyond page edges
        var pageSize = new PixelSize(500, 500);
        PixelRect[] images = [new PixelRect(-100, -100, 800, 800)];

        // Act
        double coverage = ScannedPageDetector.CalculateImageCoverage(pageSize, images);
        bool isScanned = ScannedPageDetector.IsScannedPage(pageSize, images);

        // Assert
        Assert.AreEqual(1.0, coverage, 0.0001);
        Assert.IsTrue(isScanned);
    }
}
