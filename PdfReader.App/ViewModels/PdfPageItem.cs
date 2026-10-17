using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using PdfReader.Core;

namespace PdfReader.App.ViewModels;

public sealed partial class PdfPageItem : ObservableObject
{
    public PdfPageItem(int pageIndex, PdfPageSize pageSize, double zoom = 1.0)
    {
        PageIndex = pageIndex;
        PageSize = pageSize;
        Zoom = zoom;
    }

    public int PageIndex { get; }

    public int PageNumber => PageIndex + 1;

    public PdfPageSize PageSize { get; }

    [ObservableProperty]
    public partial double Zoom { get; set; }

    [ObservableProperty]
    public partial ImageSource? RenderedImage { get; set; }

    [ObservableProperty]
    public partial bool IsRendering { get; set; }

    public double RenderedScale { get; set; }

    public double DisplayWidth => PageSize.WidthDip * Zoom;

    public double DisplayHeight => PageSize.HeightDip * Zoom;

    partial void OnZoomChanged(double value)
    {
        OnPropertyChanged(nameof(DisplayWidth));
        OnPropertyChanged(nameof(DisplayHeight));
    }
}
