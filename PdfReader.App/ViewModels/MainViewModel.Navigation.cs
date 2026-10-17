using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using PdfReader.Core;

namespace PdfReader.App.ViewModels;

public sealed partial class MainViewModel
{
    private IReadOnlyList<DocumentViewState> _bookmarkedDocumentStates = [];
    private string? _bookmarkSaveError;
    private OutlineNodeViewModel? _activeOutlineItem;

    public ObservableCollection<OutlineNodeViewModel> OutlineItems { get; } = [];

    public ObservableCollection<BookmarkItemViewModel> Bookmarks { get; } = [];

    [ObservableProperty]
    public partial bool IsNavigationPaneOpen { get; set; }

    [ObservableProperty]
    public partial bool IsBookmarksTabSelected { get; set; }

    [ObservableProperty]
    public partial string BookmarkTitleInput { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsCurrentPageBookmarked { get; set; }

    public Visibility NavigationPaneVisibility => HasDocument && IsNavigationPaneOpen
        ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ContentsTabVisibility => IsBookmarksTabSelected ? Visibility.Collapsed : Visibility.Visible;

    public Visibility BookmarksTabVisibility => IsBookmarksTabSelected ? Visibility.Visible : Visibility.Collapsed;

    public bool IsContentsTabSelected => !IsBookmarksTabSelected;

    public Visibility EmptyOutlineVisibility => OutlineItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EmptyBookmarksVisibility => Bookmarks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    [RelayCommand]
    public void ToggleNavigationPane() => IsNavigationPaneOpen = !IsNavigationPaneOpen;

    [RelayCommand]
    public void ShowContents() => IsBookmarksTabSelected = false;

    [RelayCommand]
    public void ShowBookmarks() => IsBookmarksTabSelected = true;

    [RelayCommand]
    public void NavigateToOutline(OutlineNodeViewModel? item)
    {
        if (item?.PageNumber is int pageNumber)
        {
            _activeOutlineItem = item;
            NavigateToPageNumber(pageNumber, item.Destination);
            UpdateNavigationIndicators();
        }
    }

    [RelayCommand]
    public void NavigateToBookmark(BookmarkItemViewModel? item)
    {
        if (item is not null)
        {
            _activeOutlineItem = null;
            NavigateToPageNumber(item.PageNumber);
            UpdateNavigationIndicators();
        }
    }

    [RelayCommand]
    public async Task SaveBookmarkAsync()
    {
        if (!HasDocument || IsBusy)
        {
            return;
        }

        string title = string.IsNullOrWhiteSpace(BookmarkTitleInput)
            ? string.Format(CultureInfo.CurrentCulture, GetString("BookmarkPageFormat", "Page {0}"), CurrentPageNumber)
            : BookmarkTitleInput.Trim();
        DocumentViewState state = CreateBookmarkState().WithBookmark(new(CurrentPageNumber, title, DateTimeOffset.UtcNow));
        LoadBookmarks(state.Bookmarks);
        PersistCurrentDocumentState();
        await VerifyBookmarkSavedAsync();
    }

    [RelayCommand]
    public async Task ToggleCurrentPageBookmarkAsync()
    {
        if (!HasDocument || IsBusy)
        {
            return;
        }

        BookmarkItemViewModel? existing = Bookmarks.FirstOrDefault(item => item.PageNumber == CurrentPageNumber);
        if (existing is not null)
        {
            await RemoveBookmarkAsync(existing);
        }
        else
        {
            await SaveBookmarkAsync();
        }
    }

    [RelayCommand]
    public async Task RemoveBookmarkAsync(BookmarkItemViewModel? item)
    {
        if (!HasDocument || IsBusy || item is null)
        {
            return;
        }

        LoadBookmarks(CreateBookmarkState().WithoutBookmark(item.PageNumber).Bookmarks);
        PersistCurrentDocumentState();
        await VerifyBookmarkSavedAsync();
    }

    private async Task VerifyBookmarkSavedAsync()
    {
        try
        {
            await _settingsService.FlushAsync();
            if (_bookmarkSaveError is not null && ErrorMessage == _bookmarkSaveError)
            {
                IsErrorOpen = false;
                ErrorMessage = null;
            }

            _bookmarkSaveError = null;
        }
        catch (IOException)
        {
            _bookmarkSaveError = GetString("BookmarkSaveFailed", "Could not save bookmarks. Check access to your local settings folder and try again.");
            ErrorMessage = _bookmarkSaveError;
            IsErrorOpen = true;
        }
    }

    private DocumentViewState CreateBookmarkState() => new(
        _currentFilePath ?? string.Empty, CurrentPageNumber, Zoom, ActiveZoomMode,
        DateTimeOffset.UtcNow, GetBookmarksSnapshot());

    private IReadOnlyList<PdfBookmarkItem> GetBookmarksSnapshot() => Bookmarks
        .Select(item => new PdfBookmarkItem(item.PageNumber, item.Title, item.CreatedUtc)).ToArray();

    private void LoadDocumentNavigation(IReadOnlyList<PdfOutlineItem> outline, IReadOnlyList<PdfBookmarkItem>? bookmarks)
    {
        OutlineItems.Clear();
        foreach (PdfOutlineItem item in PdfOutlineItem.Flatten(outline))
        {
            string title = string.IsNullOrWhiteSpace(item.Title) ? GetString("OutlineUntitled", "Untitled section") : item.Title;
            OutlineItems.Add(new OutlineNodeViewModel(title, item.PageIndex, item.Level, item.NormalizedX, item.NormalizedY));
        }

        OnPropertyChanged(nameof(EmptyOutlineVisibility));
        LoadBookmarks(bookmarks);
    }

    private void LoadBookmarks(IReadOnlyList<PdfBookmarkItem>? bookmarks)
    {
        Bookmarks.Clear();
        foreach (PdfBookmarkItem bookmark in (bookmarks ?? []).Where(item => item.PageNumber >= 1 && item.PageNumber <= TotalPages)
            .DistinctBy(item => item.PageNumber).OrderBy(item => item.PageNumber))
        {
            string title = string.IsNullOrWhiteSpace(bookmark.Title)
                ? string.Format(CultureInfo.CurrentCulture, GetString("BookmarkPageFormat", "Page {0}"), bookmark.PageNumber)
                : bookmark.Title;
            Bookmarks.Add(new BookmarkItemViewModel(bookmark.PageNumber, title, bookmark.CreatedUtc));
        }

        OnPropertyChanged(nameof(EmptyBookmarksVisibility));
        UpdateNavigationIndicators();
    }

    private void ResetDocumentNavigation()
    {
        OutlineItems.Clear();
        Bookmarks.Clear();
        _activeOutlineItem = null;
        BookmarkTitleInput = string.Empty;
        IsCurrentPageBookmarked = false;
        OnPropertyChanged(nameof(EmptyOutlineVisibility));
        OnPropertyChanged(nameof(EmptyBookmarksVisibility));
    }

    private void UpdateNavigationIndicators()
    {
        OutlineNodeViewModel? activeSection = null;
        foreach (OutlineNodeViewModel item in OutlineItems)
        {
            item.IsCurrentSection = false;
            if (item.PageNumber is int page && page <= CurrentPageNumber &&
                (activeSection?.PageNumber is not int activePage || page > activePage))
            {
                activeSection = item;
            }
        }

        if (_activeOutlineItem?.PageNumber == CurrentPageNumber)
        {
            activeSection = _activeOutlineItem;
        }

        if (activeSection is not null)
        {
            activeSection.IsCurrentSection = true;
        }

        foreach (BookmarkItemViewModel item in Bookmarks)
        {
            item.IsCurrentPage = item.PageNumber == CurrentPageNumber;
        }

        BookmarkItemViewModel? current = Bookmarks.FirstOrDefault(item => item.IsCurrentPage);
        IsCurrentPageBookmarked = current is not null;
        BookmarkTitleInput = current?.Title ?? string.Empty;
    }

    partial void OnCurrentPageNumberChanged(int value)
    {
        if (_activeOutlineItem?.PageNumber != value)
        {
            _activeOutlineItem = null;
        }

        UpdateNavigationIndicators();
    }

    partial void OnIsNavigationPaneOpenChanged(bool value) => OnPropertyChanged(nameof(NavigationPaneVisibility));

    partial void OnIsBookmarksTabSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(ContentsTabVisibility));
        OnPropertyChanged(nameof(BookmarksTabVisibility));
        OnPropertyChanged(nameof(IsContentsTabSelected));
    }
}
