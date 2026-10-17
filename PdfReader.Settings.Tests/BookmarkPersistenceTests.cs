using Microsoft.VisualStudio.TestTools.UnitTesting;
using PdfReader.App.Services;
using PdfReader.Core;

namespace PdfReader.Settings.Tests;

[TestClass]
[DoNotParallelize]
public sealed class BookmarkPersistenceTests
{
    [TestMethod]
    public void RecentFileEvictionAndClearingPreserveBookmarks()
    {
        DocumentViewState bookmarked = CreateState("bookmarked.pdf", [new(2, "Chapter two", DateTimeOffset.UtcNow)]);
        ReaderSettings settings = new ReaderSettings().WithUpdatedDocumentState(bookmarked);
        for (int i = 0; i < ReaderSettings.MaxRecentDocuments + 1; i++)
        {
            settings = settings.WithUpdatedDocumentState(CreateState($"recent-{i}.pdf"));
        }

        Assert.AreEqual(ReaderSettings.MaxRecentDocuments, settings.RecentDocuments.Count);
        Assert.IsFalse(settings.RecentDocuments.Any(item => item.FilePath == bookmarked.FilePath));
        Assert.AreEqual("Chapter two", settings.FindDocumentState(bookmarked.FilePath)!.Bookmarks![0].Title);

        ReaderSettings cleared = settings with { RecentDocuments = [] };
        Assert.AreEqual("Chapter two", cleared.FindDocumentState(bookmarked.FilePath)!.Bookmarks![0].Title);
    }

    [TestMethod]
    public void RemovingFinalBookmarkDoesNotResurrectArchivedCopy()
    {
        DocumentViewState bookmarked = CreateState("sample.pdf", [new(2, "Chapter two", DateTimeOffset.UtcNow)]);
        ReaderSettings settings = new ReaderSettings().WithUpdatedDocumentState(bookmarked);

        settings = settings.WithUpdatedDocumentState(bookmarked.WithoutBookmark(2));
        settings = settings with { RecentDocuments = [] };

        Assert.AreEqual(0, settings.BookmarkedDocuments.Count);
        Assert.IsNull(settings.FindDocumentState(bookmarked.FilePath));
    }

    [TestMethod]
    public void BookmarksAreIsolatedByFullPathAndPathMatchingIgnoresCase()
    {
        DocumentViewState first = CreateState(Path.Combine("first", "sample.pdf"), [new(1, "First file", DateTimeOffset.UtcNow)]);
        DocumentViewState second = CreateState(Path.Combine("second", "sample.pdf"), [new(1, "Second file", DateTimeOffset.UtcNow)]);
        ReaderSettings settings = new ReaderSettings().WithUpdatedDocumentState(first).WithUpdatedDocumentState(second);

        Assert.AreEqual("First file", settings.FindDocumentState(first.FilePath.ToUpperInvariant())!.Bookmarks![0].Title);
        Assert.AreEqual("Second file", settings.FindDocumentState(second.FilePath)!.Bookmarks![0].Title);
        Assert.AreEqual(2, settings.BookmarkedDocuments.Count);
    }

    [TestMethod]
    public async Task SaveAndReloadPreserveBookmarkMetadataAndAppearance()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, "settings.json");
            DocumentViewState state = CreateState("unicode.pdf", [new(2, "Bab \u03a9", DateTimeOffset.UtcNow)]);
            ReaderSettings settings = (new ReaderSettings { UiTheme = AppUiTheme.Light, ImageDimPercentage = 25 })
                .WithUpdatedDocumentState(state);
            var service = new ReaderSettingsService(path);

            service.Save(settings);
            await service.FlushAsync();
            ReaderSettings reloaded = new ReaderSettingsService(path).Load();

            Assert.AreEqual(state.Bookmarks![0], reloaded.FindDocumentState(state.FilePath)!.Bookmarks![0]);
            Assert.AreEqual(AppUiTheme.Light, reloaded.UiTheme);
            Assert.AreEqual(25, reloaded.ImageDimPercentage);
            Assert.AreEqual(1, reloaded.BookmarkedDocuments.Count);
            Assert.IsFalse(File.Exists(path + ".tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task ConsecutiveSavesKeepLatestSnapshot()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, "settings.json");
            var service = new ReaderSettingsService(path);
            for (int i = 0; i < 20; i++)
            {
                service.Save(new ReaderSettings { ImageDimPercentage = i });
            }

            await service.FlushAsync();

            Assert.AreEqual(19, new ReaderSettingsService(path).Load().ImageDimPercentage);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task SaveFailureIsReportedAndExistingSettingsRemainReadable()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, "settings.json");
            var service = new ReaderSettingsService(path);
            service.Save(new ReaderSettings { ImageDimPercentage = 15 });
            await service.FlushAsync();
            Directory.CreateDirectory(path + ".tmp");

            service.Save(new ReaderSettings { ImageDimPercentage = 30 });
            await Assert.ThrowsAsync<IOException>(() => service.FlushAsync());

            Assert.AreEqual(15, new ReaderSettingsService(path).Load().ImageDimPercentage);
            Directory.Delete(path + ".tmp");
            service.Save(new ReaderSettings { ImageDimPercentage = 40 });
            await service.FlushAsync();
            Assert.AreEqual(40, new ReaderSettingsService(path).Load().ImageDimPercentage);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task LegacySettingsWithoutBookmarksStillLoad()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, "settings.json");
            await File.WriteAllTextAsync(path,
                """{"UiTheme":1,"RecentDocuments":[{"FilePath":"legacy.pdf","LastPageNumber":3,"Zoom":1.5,"ZoomMode":0,"LastOpenedUtc":"2026-01-01T00:00:00Z"}]}""");

            ReaderSettings settings = new ReaderSettingsService(path).Load();

            Assert.AreEqual(AppUiTheme.Light, settings.UiTheme);
            Assert.AreEqual(3, settings.RecentDocuments[0].LastPageNumber);
            Assert.IsNull(settings.RecentDocuments[0].Bookmarks);
            Assert.AreEqual(0, settings.BookmarkedDocuments.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static DocumentViewState CreateState(string fileName, IReadOnlyList<PdfBookmarkItem>? bookmarks = null) =>
        new(Path.GetFullPath(fileName), 2, 1.25, ZoomMode.Custom, DateTimeOffset.UtcNow, bookmarks);

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"pdf-reader-settings-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
