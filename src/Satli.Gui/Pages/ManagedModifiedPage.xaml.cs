using Microsoft.UI.Xaml.Controls;
using Satli_Gui.Controls;
using Satli_Gui.Models;

namespace Satli_Gui.Pages;

public sealed partial class ManagedModifiedPage : Page
{
    public ManagedModifiedPage()
    {
        InitializeComponent();
        Content = new ManagedGamesView(ManagedGameFilter.Modified, NavigateFromManagedPage);
    }

    private void NavigateFromManagedPage(Type destination, object parameter) =>
        Frame.Navigate(destination, parameter);
}
