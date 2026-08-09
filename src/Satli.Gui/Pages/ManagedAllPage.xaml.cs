using Microsoft.UI.Xaml.Controls;
using Satli_Gui.Controls;
using Satli_Gui.Models;

namespace Satli_Gui.Pages;

public sealed partial class ManagedAllPage : Page
{
    public ManagedAllPage()
    {
        InitializeComponent();
        Content = new ManagedGamesView(ManagedGameFilter.All, NavigateFromManagedPage);
    }

    private void NavigateFromManagedPage(Type destination, object parameter) =>
        Frame.Navigate(destination, parameter);
}
