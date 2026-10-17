using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace PdfReader.App.ViewModels;

public sealed partial class BookmarkItemViewModel : ObservableObject
{
    public BookmarkItemViewModel(int pageNumber, string title, DateTimeOffset createdUtc)
    {
        PageNumber = Math.Max(1, pageNumber);
        Title = title.Trim();
        CreatedUtc = createdUtc;
    }

    public int PageNumber { get; }

    public string Title { get; }

    public DateTimeOffset CreatedUtc { get; }

    public string PageBadgeText => PageNumber.ToString(System.Globalization.CultureInfo.CurrentCulture);

    [ObservableProperty]
    public partial bool IsCurrentPage { get; set; }

    public Visibility ActiveIndicatorVisibility => IsCurrentPage ? Visibility.Visible : Visibility.Collapsed;

    partial void OnIsCurrentPageChanged(bool value)
    {
        OnPropertyChanged(nameof(ActiveIndicatorVisibility));
    }
}
