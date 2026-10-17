using System;
using System.Collections.Generic;

namespace PdfReader.Core;

public sealed record DocumentViewState(
    string FilePath,
    int LastPageNumber,
    double Zoom,
    ZoomMode ZoomMode,
    DateTimeOffset LastOpenedUtc,
    IReadOnlyList<PdfBookmarkItem>? Bookmarks = null)
{
    public DocumentViewState WithBookmark(PdfBookmarkItem bookmark)
    {
        ArgumentNullException.ThrowIfNull(bookmark);
        ArgumentOutOfRangeException.ThrowIfLessThan(bookmark.PageNumber, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(bookmark.Title);

        var updated = new List<PdfBookmarkItem>();
        DateTimeOffset createdUtc = bookmark.CreatedUtc;
        foreach (PdfBookmarkItem existing in Bookmarks ?? [])
        {
            if (existing.PageNumber == bookmark.PageNumber)
            {
                createdUtc = existing.CreatedUtc;
            }
            else
            {
                updated.Add(existing);
            }
        }

        updated.Add(bookmark with { Title = bookmark.Title.Trim(), CreatedUtc = createdUtc });
        updated.Sort((left, right) => left.PageNumber.CompareTo(right.PageNumber));
        return this with { Bookmarks = updated.AsReadOnly() };
    }

    public DocumentViewState WithoutBookmark(int pageNumber)
    {
        var updated = new List<PdfBookmarkItem>();
        foreach (PdfBookmarkItem bookmark in Bookmarks ?? [])
        {
            if (bookmark.PageNumber != pageNumber)
            {
                updated.Add(bookmark);
            }
        }

        return this with { Bookmarks = updated.AsReadOnly() };
    }
}
