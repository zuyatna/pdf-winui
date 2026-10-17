using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using PdfReader.Core;

namespace PdfReader.App.ViewModels;

public sealed partial class PdfPageItem : ObservableObject
{
    private readonly Func<int, double, double, double, double, Task>? _requestSelectionAsync;
    private readonly Action? _clearSelection;

    public PdfPageItem(
        int pageIndex,
        PdfPageSize pageSize,
        double zoom = 1.0,
        Brush? pageBackgroundBrush = null,
        bool isTextSelectionMode = false,
        Func<int, double, double, double, double, Task>? requestSelectionAsync = null,
        Action? clearSelection = null)
    {
        PageIndex = pageIndex;
        PageSize = pageSize;
        Zoom = zoom;
        PageBackgroundBrush = pageBackgroundBrush;
        IsTextSelectionMode = isTextSelectionMode;
        _requestSelectionAsync = requestSelectionAsync;
        _clearSelection = clearSelection;
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

    [ObservableProperty]
    public partial Brush? PageBackgroundBrush { get; set; }

    [ObservableProperty]
    public partial bool IsTextSelectionMode { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<NormalizedRect> SearchHighlightRects { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<NormalizedRect> ActiveSearchHighlightRects { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<NormalizedRect> SelectionRects { get; set; } = [];

    public double RenderedScale { get; set; }

    public ReadingThemePalette? RenderedTheme { get; set; }

    public double DisplayWidth => PageSize.WidthDip * Zoom;

    public double DisplayHeight => PageSize.HeightDip * Zoom;

    public Task RequestTextSelectionAsync(
        double startNormalizedX,
        double startNormalizedY,
        double endNormalizedX,
        double endNormalizedY)
    {
        return _requestSelectionAsync?.Invoke(
            PageIndex,
            startNormalizedX,
            startNormalizedY,
            endNormalizedX,
            endNormalizedY) ?? Task.CompletedTask;
    }

    public void ClearTextSelection()
    {
        _clearSelection?.Invoke();
    }

    partial void OnZoomChanged(double value)
    {
        OnPropertyChanged(nameof(DisplayWidth));
        OnPropertyChanged(nameof(DisplayHeight));
    }
}
