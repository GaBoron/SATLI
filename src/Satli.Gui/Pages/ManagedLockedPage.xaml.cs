using Microsoft.UI.Xaml.Controls;
using Satli_Gui.Controls;
using Satli_Gui.Models;

namespace Satli_Gui.Pages;

public sealed partial class ManagedLockedPage : Page
{
    public ManagedLockedPage()
    {
        InitializeComponent();
        Content = new ManagedGamesView(ManagedGameFilter.Locked, NavigateFromManagedPage);
    }

    private void NavigateFromManagedPage(Type destination, object parameter) =>
        Frame.Navigate(destination, parameter);
}
