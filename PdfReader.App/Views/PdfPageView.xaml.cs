using System;
using System.Collections.Generic;
using System.ComponentModel;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PdfReader.App.ViewModels;
using PdfReader.Core;
using Windows.Foundation;

namespace PdfReader.App.Views;

public sealed partial class PdfPageView : UserControl
{
    private const double SelectionDragThresholdDip = 4.0;

    public static readonly DependencyProperty PageItemProperty =
        DependencyProperty.Register(
            nameof(PageItem),
            typeof(PdfPageItem),
            typeof(PdfPageView),
            new PropertyMetadata(null, OnPageItemChanged));

    private Point _pointerDownPosition;
    private bool _isPointerPressed;
    private bool _hasDraggedSelection;

    public PdfPageView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public PdfPageItem? PageItem
    {
        get => (PdfPageItem?)GetValue(PageItemProperty);
        set => SetValue(PageItemProperty, value);
    }

    private static void OnPageItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PdfPageView view)
        {
            return;
        }

        if (e.OldValue is PdfPageItem oldItem)
        {
            oldItem.PropertyChanged -= view.OnPageItemPropertyChanged;
        }

        if (e.NewValue is PdfPageItem newItem)
        {
            newItem.PropertyChanged -= view.OnPageItemPropertyChanged;
            newItem.PropertyChanged += view.OnPageItemPropertyChanged;
        }

        view.Bindings.Update();
        view.UpdateInteractionCursor();
        view.RebuildOverlays();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (PageItem is not null)
        {
            PageItem.PropertyChanged -= OnPageItemPropertyChanged;
            PageItem.PropertyChanged += OnPageItemPropertyChanged;
        }

        UpdateInteractionCursor();
        RebuildOverlays();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (PageItem is not null)
        {
            PageItem.PropertyChanged -= OnPageItemPropertyChanged;
        }
    }

    private void OnPageItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PdfPageItem.IsTextSelectionMode))
        {
            UpdateInteractionCursor();
            return;
        }

        if (e.PropertyName is nameof(PdfPageItem.Zoom)
            or nameof(PdfPageItem.DisplayWidth)
            or nameof(PdfPageItem.DisplayHeight)
            or nameof(PdfPageItem.SearchHighlightRects)
            or nameof(PdfPageItem.ActiveSearchHighlightRects)
            or nameof(PdfPageItem.SelectionRects))
        {
            RebuildOverlays();
        }
    }

    private void OnPageBorderPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (PageItem is null || !PageItem.IsTextSelectionMode)
        {
            return;
        }

        PointerPoint point = e.GetCurrentPoint(PageBorder);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _pointerDownPosition = point.Position;
        _isPointerPressed = true;
        _hasDraggedSelection = false;
        PageBorder.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private async void OnPageBorderPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPointerPressed || PageItem is null || !PageItem.IsTextSelectionMode)
        {
            return;
        }

        PointerPoint point = e.GetCurrentPoint(PageBorder);
        if (!point.Properties.IsLeftButtonPressed)
        {
            EndPointerSelection(e.Pointer);
            return;
        }

        Point currentPosition = point.Position;
        double dx = currentPosition.X - _pointerDownPosition.X;
        double dy = currentPosition.Y - _pointerDownPosition.Y;
        if (!_hasDraggedSelection && ((dx * dx) + (dy * dy)) < (SelectionDragThresholdDip * SelectionDragThresholdDip))
        {
            return;
        }

        _hasDraggedSelection = true;
        e.Handled = true;

        double width = Math.Max(1.0, PageItem.DisplayWidth);
        double height = Math.Max(1.0, PageItem.DisplayHeight);
        double startNormX = Math.Clamp(_pointerDownPosition.X / width, 0.0, 1.0);
        double startNormY = Math.Clamp(_pointerDownPosition.Y / height, 0.0, 1.0);
        double endNormX = Math.Clamp(currentPosition.X / width, 0.0, 1.0);
        double endNormY = Math.Clamp(currentPosition.Y / height, 0.0, 1.0);

        await PageItem.RequestTextSelectionAsync(startNormX, startNormY, endNormX, endNormY);
    }

    private void OnPageBorderPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPointerPressed)
        {
            return;
        }

        bool hadDraggedSelection = _hasDraggedSelection;
        EndPointerSelection(e.Pointer);
        e.Handled = true;

        if (!hadDraggedSelection)
        {
            PageItem?.ClearTextSelection();
        }
    }

    private void OnPageBorderPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        EndPointerSelection(e.Pointer);
    }

    private void EndPointerSelection(Pointer pointer)
    {
        _isPointerPressed = false;
        _hasDraggedSelection = false;
        PageBorder.ReleasePointerCapture(pointer);
    }

    private void UpdateInteractionCursor()
    {
        InputSystemCursorShape cursorShape = PageItem?.IsTextSelectionMode == true
            ? InputSystemCursorShape.IBeam
            : InputSystemCursorShape.Hand;
        ProtectedCursor = InputSystemCursor.Create(cursorShape);
    }

    private void RebuildOverlays()
    {
        OverlayCanvas.Children.Clear();
        if (PageItem is null)
        {
            return;
        }

        double width = PageItem.DisplayWidth;
        double height = PageItem.DisplayHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        Brush? searchBrush = TryGetThemeBrush("SystemFillColorCautionBrush");
        Brush? activeSearchBrush = TryGetThemeBrush("SystemFillColorAttentionBrush");
        Brush? selectionBrush = TryGetThemeBrush("AccentFillColorDefaultBrush");

        AddRectanglesToCanvas(PageItem.SearchHighlightRects, width, height, searchBrush, opacity: 0.35, strokeBrush: null);
        AddRectanglesToCanvas(PageItem.ActiveSearchHighlightRects, width, height, activeSearchBrush, opacity: 0.50, strokeBrush: selectionBrush);
        AddRectanglesToCanvas(PageItem.SelectionRects, width, height, selectionBrush, opacity: 0.32, strokeBrush: null);
    }

    private void AddRectanglesToCanvas(
        IReadOnlyList<NormalizedRect> rects,
        double pageWidth,
        double pageHeight,
        Brush? fillBrush,
        double opacity,
        Brush? strokeBrush)
    {
        if (rects is null || rects.Count == 0 || fillBrush is null)
        {
            return;
        }

        for (int i = 0; i < rects.Count; i++)
        {
            NormalizedRect norm = rects[i];
            if (norm.IsEmpty)
            {
                continue;
            }

            var shape = new Rectangle
            {
                Width = Math.Max(2.0, norm.Width * pageWidth),
                Height = Math.Max(2.0, norm.Height * pageHeight),
                Fill = fillBrush,
                Opacity = opacity,
                RadiusX = 2.0,
                RadiusY = 2.0,
                IsHitTestVisible = false,
            };

            if (strokeBrush is not null)
            {
                shape.Stroke = strokeBrush;
                shape.StrokeThickness = 1.5;
            }

            Canvas.SetLeft(shape, norm.X * pageWidth);
            Canvas.SetTop(shape, norm.Y * pageHeight);
            OverlayCanvas.Children.Add(shape);
        }
    }

    private static Brush? TryGetThemeBrush(string key)
    {
        if (Application.Current?.Resources is ResourceDictionary resources &&
            resources.TryGetValue(key, out object? value) &&
            value is Brush brush)
        {
            return brush;
        }

        return null;
    }
}
