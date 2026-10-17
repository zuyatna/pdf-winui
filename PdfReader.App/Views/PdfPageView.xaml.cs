using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PdfReader.App.ViewModels;

namespace PdfReader.App.Views;

public sealed partial class PdfPageView : UserControl
{
    public static readonly DependencyProperty PageItemProperty =
        DependencyProperty.Register(
            nameof(PageItem),
            typeof(PdfPageItem),
            typeof(PdfPageView),
            new PropertyMetadata(null, OnPageItemChanged));

    public PdfPageView()
    {
        InitializeComponent();
    }

    public PdfPageItem? PageItem
    {
        get => (PdfPageItem?)GetValue(PageItemProperty);
        set => SetValue(PageItemProperty, value);
    }

    private static void OnPageItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PdfPageView view)
        {
            view.Bindings.Update();
        }
    }
}
