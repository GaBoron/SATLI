using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satli_Gui.Models;
using Satli_Gui.Services;

namespace Satli_Gui.ViewModels;

public sealed partial class TranslationManagementViewModel : ObservableObject
{
    private readonly SatliCliService _cli = new();
    private readonly Func<GuiSettings> _settings;
    private readonly ApplicationOperationState _operation;
    private readonly Action<string, InfoBarSeverity> _showInfo;
    private readonly TranslationCliArguments _arguments;
    private string _searchText = string.Empty;
    private string _detectedSteamDirectory = string.Empty;
    private bool _isLoading = true;
    private GameInstallFilterOption _selectedFilterOption = GameInstallFiltering.Options[0];
    private int _updateAvailableCount;

    public event Action<int, bool>? UpdatesDetected;

    public TranslationManagementViewModel(
        Func<GuiSettings> settings,
        ApplicationOperationState operation,
        Action<string, InfoBarSeverity> showInfo)
    {
        _settings = settings;
        _operation = operation;
        _showInfo = showInfo;
        _arguments = new TranslationCliArguments(settings, () => DetectedSteamDirectory);
    }

    public ObservableCollection<GameItem> Games { get; } = [];
    public ObservableCollection<GameItem> VisibleGames { get; } = [];
    public ObservableCollection<GameItem> ManagedGames { get; } = [];
    public IReadOnlyList<GameInstallFilterOption> FilterOptions => GameInstallFiltering.Options;
    public int SelectedCount => Games.Count(item => item.IsSelected);
    public string SelectedCountText => $"已选 {SelectedCount} 项";
    public string SelectionActionText =>
        GameSelectionOperations.AreAllSelected(VisibleGames) ? "取消全选" : "全选";
    public int UpdateAvailableCount
    {
        get => _updateAvailableCount;
        private set => SetProperty(ref _updateAvailableCount, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(LoadingVisibility));
                OnPropertyChanged(nameof(GameListVisibility));
                OnPropertyChanged(nameof(EmptyStateVisibility));
                OnPropertyChanged(nameof(ManagedGameListVisibility));
                OnPropertyChanged(nameof(ManagedEmptyStateVisibility));
            }
        }
    }

    public Visibility LoadingVisibility => IsLoading ? Visibility.Visible : Visibility.Collapsed;
    public Visibility GameListVisibility => IsLoading ? Visibility.Collapsed : Visibility.Visible;
    public Visibility EmptyStateVisibility => !IsLoading && VisibleGames.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility ManagedGameListVisibility => IsLoading ? Visibility.Collapsed : Visibility.Visible;
    public Visibility ManagedEmptyStateVisibility => !IsLoading && ManagedGames.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public GameInstallFilterOption SelectedFilterOption
    {
        get => _selectedFilterOption;
        set
        {
            if (value is null || !SetProperty(ref _selectedFilterOption, value))
            {
                return;
            }
            ClearSelection();
            ApplyFilter();
        }
    }

    public string DetectedSteamDirectory
    {
        get => _detectedSteamDirectory;
        private set => SetProperty(ref _detectedSteamDirectory, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilter();
            }
        }
    }

    public void ShowInfo(string message, InfoBarSeverity severity = InfoBarSeverity.Informational) =>
        _showInfo(message, severity);

    private void ApplyFilter()
    {
        VisibleGames.Clear();
        var query = SearchText.Trim();
        foreach (var game in Games.Where(game =>
                     GameInstallFiltering.Matches(game, SelectedFilterOption.Value)
                     && (string.IsNullOrWhiteSpace(query)
                         || game.GameName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                         || game.AppId.Contains(query, StringComparison.OrdinalIgnoreCase))))
        {
            VisibleGames.Add(game);
        }
        OnPropertyChanged(nameof(SelectionActionText));
        OnPropertyChanged(nameof(EmptyStateVisibility));
    }

    internal void BeginLoading()
    {
        IsLoading = true;
    }

    internal void CompleteLoading() => IsLoading = false;

    public void ToggleVisibleSelection()
    {
        GameSelectionOperations.ToggleVisible(VisibleGames);
        RefreshSelectionCount();
    }

    public void ClearSelection()
    {
        GameSelectionOperations.ClearAll(Games);
        RefreshSelectionCount();
    }

    public void RefreshSelectionCount()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedCountText));
        OnPropertyChanged(nameof(SelectionActionText));
    }

    public void ShowUpdates() => SelectedFilterOption = GameInstallFiltering.UpdateOption;

    private void ShowException(string operation, Exception exception)
    {
        _ = App.Logs.WriteExceptionDetailsAsync(operation, exception);
        var message = NetworkErrorMessage.IsNetworkError(exception)
            ? NetworkErrorMessage.Describe(exception, operation)
            : exception.Message;
        ShowInfo(message, InfoBarSeverity.Error);
    }

    private void ShowResultError(CliRunResult result) =>
        ShowInfo(ResultError(result), InfoBarSeverity.Error);

    private static string ResultError(CliRunResult result) =>
        string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? $"SATLI 操作失败，退出码 {result.ExitCode}。"
            : result.ErrorMessage;
}
