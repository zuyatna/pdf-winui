using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PdfReader.Core.Tests;

[TestClass]
public sealed class ZoomCalculatorTests
{
    [TestMethod]
    public void CalculateFitWidthZoom_WithLetterPage_ReturnsExpectedZoomRatio()
    {
        // Arrange: US Letter is 612 pt = 816 DIPs wide
        var letterPage = new PdfPageSize(612, 792);

        // Act: 816 * 1.25 + 48 padding = 1068 DIP viewport width
        double zoom = ZoomCalculator.CalculateFitWidthZoom(letterPage, viewportWidthDip: 1068, horizontalPaddingDip: 48);

        // Assert
        Assert.AreEqual(1.25, zoom, 0.001);
    }

    [TestMethod]
    public void CalculateFitPageZoom_UsesSmallerOfWidthAndHeightRatios()
    {
        // Arrange: US Letter is 816 x 1056 DIPs
        var letterPage = new PdfPageSize(612, 792);

        // Act: width allows 1.5x, height allows 0.5x (528 + 32 = 560 DIPs)
        double zoom = ZoomCalculator.CalculateFitPageZoom(
            letterPage,
            viewportWidthDip: 1300,
            viewportHeightDip: 560,
            horizontalPaddingDip: 48,
            verticalPaddingDip: 32);

        // Assert
        Assert.AreEqual(0.50, zoom, 0.001);
    }

    [TestMethod]
    public void StepZoom_ClampsBetweenMinAndMaxBounds()
    {
        // Act & Assert
        Assert.AreEqual(1.10, ZoomCalculator.StepZoom(1.0, steps: 1), 0.001);
        Assert.AreEqual(0.90, ZoomCalculator.StepZoom(1.0, steps: -1), 0.001);
        Assert.AreEqual(ZoomCalculator.MinZoom, ZoomCalculator.StepZoom(0.30, steps: -5), 0.001);
        Assert.AreEqual(ZoomCalculator.MaxZoom, ZoomCalculator.StepZoom(3.95, steps: 5), 0.001);
    }
}
