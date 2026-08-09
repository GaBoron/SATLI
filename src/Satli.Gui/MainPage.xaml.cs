using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satli_Gui.Pages;
using Satli_Gui.Models;
using Satli_Gui.ViewModels;

namespace Satli_Gui;

public sealed partial class MainPage : Page
{
    private bool _initialized;
    public MainViewModel ViewModel => App.ViewModel;

    public MainPage()
    {
        InitializeComponent();
        Loaded += MainPage_Loaded;
        ContentFrame.Navigate(typeof(GamesPage));
        Navigation.SelectedItem = ManageableGamesItem;
        ViewModel.ShowUpdatesRequested += ViewModel_ShowUpdatesRequested;
    }

    private void ViewModel_ShowUpdatesRequested()
    {
        if (ContentFrame.CurrentSourcePageType != typeof(GamesPage))
        {
            ContentFrame.Navigate(typeof(GamesPage));
        }
        Navigation.SelectedItem = ManageableGamesItem;
    }

    private void InfoAction_Click(object sender, RoutedEventArgs e) => ViewModel.InvokeInfoAction();

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }
        _initialized = true;
        await ViewModel.InitializeAsync();
    }

    private void Navigation_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        var tag = (args.InvokedItemContainer as NavigationViewItem)?.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(tag))
        {
            return;
        }
        var destination = tag switch
        {
            "managed-all" or "managed-locked" => typeof(ManagedPage),
            "local" => typeof(LocalGamesPage),
            "cloud" => typeof(CloudGamesPage),
            "logs" => typeof(LogsPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(GamesPage),
        };
        var managedFilter = tag switch
        {
            "managed-all" => ManagedGameFilter.All,
            "managed-locked" => ManagedGameFilter.Locked,
            _ => (ManagedGameFilter?)null,
        };
        var managedFilterChanged = managedFilter is ManagedGameFilter filter
            && ViewModel.Translations.ManagedFilter != filter;
        if (ContentFrame.CurrentSourcePageType != destination || managedFilterChanged)
        {
            ContentFrame.Navigate(destination, managedFilter);
        }
        if (Navigation.DisplayMode == NavigationViewDisplayMode.Minimal)
        {
            Navigation.IsPaneOpen = false;
        }
    }
}
