using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Satli_Gui.Controls;

public sealed partial class TranslationAssetBadges : UserControl
{
    public static readonly DependencyProperty HasBinProperty = DependencyProperty.Register(
        nameof(HasBin), typeof(bool), typeof(TranslationAssetBadges), new PropertyMetadata(false, AvailabilityChanged));
    public static readonly DependencyProperty HasJsonProperty = DependencyProperty.Register(
        nameof(HasJson), typeof(bool), typeof(TranslationAssetBadges), new PropertyMetadata(false, AvailabilityChanged));

    public TranslationAssetBadges() => InitializeComponent();

    public bool HasBin
    {
        get => (bool)GetValue(HasBinProperty);
        set => SetValue(HasBinProperty, value);
    }

    public bool HasJson
    {
        get => (bool)GetValue(HasJsonProperty);
        set => SetValue(HasJsonProperty, value);
    }

    private static void AvailabilityChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var badges = (TranslationAssetBadges)sender;
        badges.BinBadge.Visibility = badges.HasBin ? Visibility.Visible : Visibility.Collapsed;
        badges.JsonBadge.Visibility = badges.HasJson ? Visibility.Visible : Visibility.Collapsed;
    }
}
