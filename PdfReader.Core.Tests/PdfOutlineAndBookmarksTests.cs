using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PdfReader.Core.Tests;

[TestClass]
public sealed class PdfOutlineAndBookmarksTests
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
    public async Task GetOutlineAsync_WhenDocumentHasNoOutline_ReturnsEmptyList()
    {
        // Arrange
        await using var service = new PdfDocumentService();
        await service.OpenDocumentAsync(GetSamplePdfPath());

        // Act
        IReadOnlyList<PdfOutlineItem> outline = await service.GetOutlineAsync();

        // Assert
        Assert.AreEqual(0, outline.Count);
    }

    [TestMethod]
    public async Task GetOutlineAsync_WhenDocumentHasHierarchicalOutline_ExtractsTreeAndFlattensInOrder()
    {
        // Arrange
        string tempPdfPath = Path.Combine(Path.GetTempPath(), $"outline-test-{Guid.NewGuid():N}.pdf");
        try
        {
            File.WriteAllBytes(tempPdfPath, BuildPdfWithHierarchicalOutlines());

            await using var service = new PdfDocumentService();
            PdfDocumentMetadata metadata = await service.OpenDocumentAsync(tempPdfPath);
            Assert.AreEqual(3, metadata.PageCount);

            // Act
            IReadOnlyList<PdfOutlineItem> roots = await service.GetOutlineAsync();
            IReadOnlyList<PdfOutlineItem> flat = PdfOutlineItem.Flatten(roots);

            // Assert
            Assert.AreEqual(2, roots.Count);

            Assert.AreEqual("Chapter 1: Introduction", roots[0].Title);
            Assert.AreEqual(0, roots[0].PageIndex);
            Assert.AreEqual(1, roots[0].PageNumber);
            Assert.AreEqual(0, roots[0].Level);
            Assert.AreEqual(1, roots[0].Children.Count);

            PdfOutlineItem child = roots[0].Children[0];
            Assert.AreEqual("Section 1.1: Architecture", child.Title);
            Assert.AreEqual(1, child.PageIndex);
            Assert.AreEqual(2, child.PageNumber);
            Assert.AreEqual(1, child.Level);
            Assert.AreEqual(0, child.Children.Count);

            Assert.AreEqual("Chapter 2: Dark Mode", roots[1].Title);
            Assert.AreEqual(2, roots[1].PageIndex);
            Assert.AreEqual(3, roots[1].PageNumber);
            Assert.AreEqual(0, roots[1].Level);
            Assert.AreEqual(0, roots[1].Children.Count);

            Assert.AreEqual(3, flat.Count);
            Assert.AreEqual("Chapter 1: Introduction", flat[0].Title);
            Assert.AreEqual("Section 1.1: Architecture", flat[1].Title);
            Assert.AreEqual("Chapter 2: Dark Mode", flat[2].Title);
        }
        finally
        {
            if (File.Exists(tempPdfPath))
            {
                File.Delete(tempPdfPath);
            }
        }
    }

    [TestMethod]
    public void DocumentViewState_WithBookmarks_PreservesBookmarkMetadata()
    {
        // Arrange
        DateTimeOffset now = DateTimeOffset.UtcNow;
        IReadOnlyList<PdfBookmarkItem> bookmarks =
        [
            new PdfBookmarkItem(2, "Architecture Overview", now),
            new PdfBookmarkItem(5, "Luminance Formula", now),
        ];

        // Act
        var state = new DocumentViewState(
            @"C:\Docs\sample.pdf",
            LastPageNumber: 5,
            Zoom: 1.25,
            ZoomMode.Custom,
            now,
            bookmarks);

        // Assert
        Assert.IsNotNull(state.Bookmarks);
        Assert.AreEqual(2, state.Bookmarks.Count);
        Assert.AreEqual(2, state.Bookmarks[0].PageNumber);
        Assert.AreEqual("Architecture Overview", state.Bookmarks[0].Title);
        Assert.AreEqual(5, state.Bookmarks[1].PageNumber);
        Assert.AreEqual("Luminance Formula", state.Bookmarks[1].Title);
    }

    [TestMethod]
    public void WithBookmark_RenamesWithoutDuplicatesAndKeepsCreationTime()
    {
        var created = DateTimeOffset.UtcNow.AddDays(-1);
        var state = new DocumentViewState("sample.pdf", 1, 1, ZoomMode.Custom, created,
            [new(5, "Original", created)]);

        DocumentViewState updated = state.WithBookmark(new(5, "Renamed", DateTimeOffset.UtcNow))
            .WithBookmark(new(2, "Earlier page", created));

        Assert.AreEqual(2, updated.Bookmarks!.Count);
        Assert.AreEqual(2, updated.Bookmarks[0].PageNumber);
        Assert.AreEqual("Renamed", updated.Bookmarks[1].Title);
        Assert.AreEqual(created, updated.Bookmarks[1].CreatedUtc);
        Assert.AreEqual("Original", state.Bookmarks![0].Title);
    }

    [TestMethod]
    public void WithoutBookmark_RemovesOnlyRequestedPage()
    {
        var state = new DocumentViewState("sample.pdf", 1, 1, ZoomMode.Custom, DateTimeOffset.UtcNow)
            .WithBookmark(new(2, "Second", DateTimeOffset.UtcNow))
            .WithBookmark(new(3, "Third", DateTimeOffset.UtcNow));

        DocumentViewState updated = state.WithoutBookmark(2);

        Assert.AreEqual(1, updated.Bookmarks!.Count);
        Assert.AreEqual(3, updated.Bookmarks[0].PageNumber);
        Assert.AreEqual(2, state.Bookmarks!.Count);
    }

    [TestMethod]
    public void WithBookmark_InvalidPageIsRejected()
    {
        var state = new DocumentViewState("sample.pdf", 1, 1, ZoomMode.Custom, DateTimeOffset.UtcNow);
        Assert.Throws<ArgumentOutOfRangeException>(() => state.WithBookmark(new(0, "Invalid", DateTimeOffset.UtcNow)));
    }

    [TestMethod]
    public void DocumentViewState_JsonRoundTripAndLegacySettingsAreSupported()
    {
        var state = new DocumentViewState("sample.pdf", 2, 1.25, ZoomMode.Custom, DateTimeOffset.UtcNow,
            [new(2, "Reading notes", DateTimeOffset.UtcNow)]);

        DocumentViewState restored = JsonSerializer.Deserialize<DocumentViewState>(JsonSerializer.Serialize(state))!;
        DocumentViewState legacy = JsonSerializer.Deserialize<DocumentViewState>(
            """{"FilePath":"old.pdf","LastPageNumber":1,"Zoom":1,"ZoomMode":0,"LastOpenedUtc":"2026-01-01T00:00:00Z"}""")!;

        Assert.AreEqual(state.Bookmarks![0], restored.Bookmarks![0]);
        Assert.AreEqual(state.Zoom, restored.Zoom);
        Assert.IsNull(legacy.Bookmarks);
    }

    [TestMethod]
    public async Task GetOutlineAsync_NoDocumentIsRejected()
    {
        await using var service = new PdfDocumentService();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetOutlineAsync());
    }

    [TestMethod]
    public async Task GetOutlineAsync_PreCanceledRequestDoesNotCloseDocument()
    {
        await using var service = new PdfDocumentService();
        await service.OpenDocumentAsync(GetSamplePdfPath());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.GetOutlineAsync(cts.Token));
        Assert.IsNotNull(service.CurrentDocument);
        Assert.AreEqual(0, (await service.GetOutlineAsync()).Count);
    }

    [TestMethod]
    public async Task GetOutlineAsync_HeadingWithoutDestinationPreservesChildrenAndUnicodeTitle()
    {
        IReadOnlyList<PdfOutlineItem> roots = await ReadFixtureOutlineAsync(BuildPdfWithHierarchicalOutlines(
            firstOutline: "7 0 obj\n<< /Title <FEFF0042006100620020003100202014002003A9> /Parent 6 0 R /Next 9 0 R /First 8 0 R /Last 8 0 R /Count 1 >>\nendobj\n"));

        Assert.AreEqual("Bab 1 \u2014 \u03a9", roots[0].Title);
        Assert.IsNull(roots[0].PageIndex);
        Assert.AreEqual(1, roots[0].Children[0].PageIndex);
        Assert.AreEqual(2, roots.Count);
    }

    [TestMethod]
    [DataRow("/A << /S /URI /URI (https://example.com) >>")]
    [DataRow("/A << /S /GoToR /F (other.pdf) /D [0 /Fit] >>")]
    [DataRow("/Dest [99 /Fit]")]
    public async Task GetOutlineAsync_ExternalOrInvalidDestinationHasNoLocalPage(string destination)
    {
        IReadOnlyList<PdfOutlineItem> roots = await ReadFixtureOutlineAsync(BuildPdfWithHierarchicalOutlines(
            lastOutline: $"9 0 obj\n<< /Title (Other target) /Parent 6 0 R /Prev 7 0 R {destination} >>\nendobj\n"));

        Assert.AreEqual(2, roots.Count);
        Assert.AreEqual("Other target", roots[1].Title);
        Assert.IsNull(roots[1].PageIndex);
    }

    [TestMethod]
    public async Task GetOutlineAsync_CircularSiblingReferenceTerminatesWithoutDuplicates()
    {
        IReadOnlyList<PdfOutlineItem> roots = await ReadFixtureOutlineAsync(BuildPdfWithHierarchicalOutlines(
            lastOutline: "9 0 obj\n<< /Title (Loop) /Parent 6 0 R /Prev 7 0 R /Next 7 0 R /Dest [5 0 R /Fit] >>\nendobj\n"));

        Assert.AreEqual(2, roots.Count);
        Assert.AreEqual(3, PdfOutlineItem.Flatten(roots).Count);
    }

    [TestMethod]
    public async Task GetOutlineAsync_ClosedOrDisposedServiceIsRejected()
    {
        var service = new PdfDocumentService();
        try
        {
            await service.OpenDocumentAsync(GetSamplePdfPath());
            await service.CloseDocumentAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetOutlineAsync());
        }
        finally
        {
            await service.DisposeAsync();
        }

        Assert.Throws<ObjectDisposedException>(() => service.GetOutlineAsync());
    }

    private static async Task<IReadOnlyList<PdfOutlineItem>> ReadFixtureOutlineAsync(byte[] bytes)
    {
        string path = Path.Combine(Path.GetTempPath(), $"outline-test-{Guid.NewGuid():N}.pdf");
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            await using var service = new PdfDocumentService();
            await service.OpenDocumentAsync(path);
            return await service.GetOutlineAsync();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    [DataRow("/XYZ 72 600 null", 72.0 / 612, 192.0 / 792)]
    [DataRow("/FitH 500", 0.0, 292.0 / 792)]
    [DataRow("/FitBH 500", 0.0, 292.0 / 792)]
    [DataRow("/FitR 72 100 400 600", 72.0 / 612, 192.0 / 792)]
    public async Task GetOutlineAsync_PreservesPositionWithinDestinationPage(string view, double expectedX, double expectedY)
    {
        IReadOnlyList<PdfOutlineItem> roots = await ReadFixtureOutlineAsync(BuildPdfWithHierarchicalOutlines(
            lastOutline: $"9 0 obj\n<< /Title (Subheading) /Parent 6 0 R /Prev 7 0 R /A << /S /GoTo /D [5 0 R {view}] >> >>\nendobj\n"));

        Assert.AreEqual(2, roots[1].PageIndex);
        Assert.AreEqual(expectedY, roots[1].NormalizedY!.Value, 0.00002);
        if (view.StartsWith("/XYZ", StringComparison.Ordinal) || view.StartsWith("/FitR", StringComparison.Ordinal))
        {
            Assert.AreEqual(expectedX, roots[1].NormalizedX!.Value, 0.00002);
        }
    }

    [TestMethod]
    public async Task GetOutlineAsync_RotatedCropBoxUsesRenderedPageCoordinates()
    {
        IReadOnlyList<PdfOutlineItem> roots = await ReadFixtureOutlineAsync(BuildPdfWithHierarchicalOutlines(
            lastOutline: "9 0 obj\n<< /Title (Rotated heading) /Parent 6 0 R /Prev 7 0 R /Dest [5 0 R /XYZ 150 600 null] >>\nendobj\n",
            lastPage: "5 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /CropBox [50 100 550 700] /Rotate 90 >>\nendobj\n"));

        Assert.AreEqual(500.0 / 600, roots[1].NormalizedX!.Value, 0.00002);
        Assert.AreEqual(100.0 / 500, roots[1].NormalizedY!.Value, 0.00002);
    }

    [TestMethod]
    public async Task GetOutlineAsync_NullCoordinatesKeepPageTopFallback()
    {
        IReadOnlyList<PdfOutlineItem> roots = await ReadFixtureOutlineAsync(BuildPdfWithHierarchicalOutlines(
            lastOutline: "9 0 obj\n<< /Title (No position) /Parent 6 0 R /Dest [5 0 R /XYZ null null null] >>\nendobj\n"));

        Assert.AreEqual(2, roots[1].PageIndex);
        Assert.IsNull(roots[1].NormalizedX);
        Assert.IsNull(roots[1].NormalizedY);
    }

    [TestMethod]
    [DataRow("/FitV 72")]
    [DataRow("/FitBV 72")]
    public async Task GetOutlineAsync_VerticalFitPreservesHorizontalDestination(string view)
    {
        IReadOnlyList<PdfOutlineItem> roots = await ReadFixtureOutlineAsync(BuildPdfWithHierarchicalOutlines(
            lastOutline: $"9 0 obj\n<< /Title (Column) /Parent 6 0 R /Dest [5 0 R {view}] >>\nendobj\n"));

        Assert.AreEqual(72.0 / 612, roots[1].NormalizedX!.Value, 0.00002);
        Assert.IsNull(roots[1].NormalizedY);
    }

    private static byte[] BuildPdfWithHierarchicalOutlines(string? firstOutline = null, string? lastOutline = null, string? lastPage = null)
    {
        var objects = new string[]
        {
            "1 0 obj\n<< /Type /Catalog /Pages 2 0 R /Outlines 6 0 R >>\nendobj\n",
            "2 0 obj\n<< /Type /Pages /Kids [3 0 R 4 0 R 5 0 R] /Count 3 >>\nendobj\n",
            "3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n",
            "4 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n",
            lastPage ?? "5 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n",
            "6 0 obj\n<< /Type /Outlines /First 7 0 R /Last 9 0 R /Count 3 >>\nendobj\n",
            firstOutline ?? "7 0 obj\n<< /Title (Chapter 1: Introduction) /Parent 6 0 R /Next 9 0 R /First 8 0 R /Last 8 0 R /Count 1 /Dest [3 0 R /Fit] >>\nendobj\n",
            "8 0 obj\n<< /Title (Section 1.1: Architecture) /Parent 7 0 R /Dest [4 0 R /Fit] >>\nendobj\n",
            lastOutline ?? "9 0 obj\n<< /Title (Chapter 2: Dark Mode) /Parent 6 0 R /Prev 7 0 R /A << /S /GoTo /D [5 0 R /Fit] >> >>\nendobj\n",
        };

        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");

        var offsets = new int[objects.Length + 1];
        for (int i = 0; i < objects.Length; i++)
        {
            offsets[i + 1] = Encoding.ASCII.GetByteCount(sb.ToString());
            sb.Append(objects[i]);
        }

        int xrefOffset = Encoding.ASCII.GetByteCount(sb.ToString());
        sb.Append(string.Create(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n"));
        sb.Append("0000000000 65535 f \n");
        for (int i = 1; i <= objects.Length; i++)
        {
            sb.Append(string.Create(CultureInfo.InvariantCulture, $"{offsets[i]:D10} 00000 n \n"));
        }

        sb.Append(string.Create(
            CultureInfo.InvariantCulture,
            $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n"));

        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
