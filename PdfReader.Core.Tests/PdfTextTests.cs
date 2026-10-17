using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PdfReader.Core.Tests;

[TestClass]
public sealed class PdfTextTests
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
    public void NormalizedRect_FromPoints_OrdersAndClampsCoordinates()
    {
        // Arrange & Act
        NormalizedRect rect = NormalizedRect.FromPoints(0.8, 0.7, -0.2, 1.5);

        // Assert
        Assert.IsFalse(rect.IsEmpty);
        Assert.AreEqual(0.0, rect.X, 0.0001);
        Assert.AreEqual(0.7, rect.Y, 0.0001);
        Assert.AreEqual(0.8, rect.Width, 0.0001);
        Assert.AreEqual(0.3, rect.Height, 0.0001);
    }

    [TestMethod]
    public async Task GetPageTextAsync_WithValidSamplePdf_ExtractsExpectedText()
    {
        // Arrange
        await using var service = new PdfDocumentService();
        await service.OpenDocumentAsync(GetSamplePdfPath());

        // Act
        string pageText = await service.GetPageTextAsync(0);

        // Assert
        Assert.IsTrue(pageText.Contains("PDF Reader Sample", StringComparison.Ordinal), $"Unexpected page text: '{pageText}'");
    }

    [TestMethod]
    public async Task SearchDocumentAsync_CaseSensitivityAndHighlightRects_WorkCorrectly()
    {
        // Arrange
        await using var service = new PdfDocumentService();
        await service.OpenDocumentAsync(GetSamplePdfPath());

        // Act
        IReadOnlyList<PdfSearchMatch> insensitiveMatches = await service.SearchDocumentAsync("sample", matchCase: false);
        IReadOnlyList<PdfSearchMatch> sensitiveMismatch = await service.SearchDocumentAsync("sample", matchCase: true);
        IReadOnlyList<PdfSearchMatch> sensitiveExact = await service.SearchDocumentAsync("Sample", matchCase: true);

        // Assert
        Assert.AreEqual(1, insensitiveMatches.Count);
        Assert.AreEqual(0, insensitiveMatches[0].PageIndex);
        Assert.AreEqual(6, insensitiveMatches[0].CharCount);
        Assert.IsTrue(insensitiveMatches[0].HighlightRects.Count > 0);
        Assert.IsFalse(insensitiveMatches[0].HighlightRects[0].IsEmpty);

        Assert.AreEqual(0, sensitiveMismatch.Count);
        Assert.AreEqual(1, sensitiveExact.Count);
    }

    [TestMethod]
    public async Task SelectTextAsync_AcrossTextRegion_ReturnsSelectedTextAndRects()
    {
        // Arrange
        await using var service = new PdfDocumentService();
        await service.OpenDocumentAsync(GetSamplePdfPath());
        IReadOnlyList<PdfSearchMatch> matches = await service.SearchDocumentAsync("PDF Reader Sample", matchCase: false);
        Assert.AreEqual(1, matches.Count);
        NormalizedRect fullLineRect = matches[0].HighlightRects[0];

        double startX = fullLineRect.X + 0.002;
        double endX = fullLineRect.X + fullLineRect.Width - 0.002;
        double midY = fullLineRect.Y + (fullLineRect.Height * 0.5);

        // Act
        PdfTextSelection? selection = await service.SelectTextAsync(0, startX, midY, endX, midY);

        // Assert
        Assert.IsNotNull(selection);
        Assert.AreEqual(0, selection.PageIndex);
        Assert.IsTrue(selection.SelectedText.Contains("PDF", StringComparison.Ordinal));
        Assert.IsTrue(selection.SelectedText.Contains("Sample", StringComparison.Ordinal));
        Assert.IsTrue(selection.HighlightRects.Count > 0);
    }
}
