using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace PdfReader.App.ViewModels;

public sealed partial class OutlineNodeViewModel : ObservableObject
{
    public OutlineNodeViewModel(string title, int? pageIndex, int level, double? normalizedX = null, double? normalizedY = null)
    {
        Title = title;
        PageIndex = pageIndex;
        Level = Math.Max(0, level);
        Destination = pageIndex is int index ? new PdfReader.Core.PdfPageDestination(index, normalizedX, normalizedY) : null;
    }

    public string Title { get; }

    public int? PageIndex { get; }

    public PdfReader.Core.PdfPageDestination? Destination { get; }

    public int? PageNumber => PageIndex.HasValue ? PageIndex.Value + 1 : null;

    public int Level { get; }

    public Thickness ItemPadding => new(12 + (Math.Min(Level, 6) * 16), 8, 12, 8);

    public string PageBadgeText => PageNumber.HasValue ? $"{PageNumber.Value}" : string.Empty;

    public Visibility PageBadgeVisibility => PageNumber.HasValue ? Visibility.Visible : Visibility.Collapsed;

    public double TitleOpacity => PageNumber.HasValue ? 1.0 : 0.7;

    [ObservableProperty]
    public partial bool IsCurrentSection { get; set; }

    public Visibility ActiveIndicatorVisibility => IsCurrentSection ? Visibility.Visible : Visibility.Collapsed;

    partial void OnIsCurrentSectionChanged(bool value)
    {
        OnPropertyChanged(nameof(ActiveIndicatorVisibility));
    }
}
