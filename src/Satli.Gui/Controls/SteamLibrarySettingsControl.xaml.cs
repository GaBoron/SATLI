using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using Satli_Gui.Models;

namespace Satli_Gui.Controls;

public sealed partial class SteamLibrarySettingsControl : UserControl
{
    private bool _isInitializing;
    private bool _isOffline;
    private bool _apiKeyChanged;
    private string _protectedApiKey = string.Empty;
    private readonly DispatcherQueueTimer _credentialSaveTimer;

    public event EventHandler? SettingsChanged;
    public event EventHandler? TestConnectionRequested;
    public event EventHandler? OpenApiKeyPageRequested;

    public SteamLibrarySettingsControl()
    {
        InitializeComponent();
        _credentialSaveTimer = DispatcherQueue.CreateTimer();
        _credentialSaveTimer.Interval = TimeSpan.FromMilliseconds(500);
        _credentialSaveTimer.IsRepeating = false;
        _credentialSaveTimer.Tick += (_, _) => NotifyChanged();
    }

    public void LoadSettings(SteamLibrarySettings settings, bool isOffline)
    {
        _isInitializing = true;
        EnabledSwitch.IsOn = settings.Enabled;
        SteamIdBox.Text = settings.SteamId;
        ApiKeyBox.Password = settings.ApiKey;
        _protectedApiKey = settings.ProtectedApiKey;
        _apiKeyChanged = false;
        _isOffline = isOffline;
        UpdateAvailability();
        _isInitializing = false;
    }

    public SteamLibrarySettings ReadSettings() => new()
    {
        Enabled = EnabledSwitch.IsOn,
        SteamId = SteamIdBox.Text,
        ApiKey = ApiKeyBox.Password,
        ApiKeyChanged = _apiKeyChanged,
        ProtectedApiKey = _protectedApiKey,
    };

    public void MarkSaved(SteamLibrarySettings settings)
    {
        if (string.Equals(ApiKeyBox.Password, settings.ApiKey, StringComparison.Ordinal))
        {
            _protectedApiKey = settings.ProtectedApiKey;
            _apiKeyChanged = false;
        }
    }

    public void SetOffline(bool isOffline)
    {
        _isOffline = isOffline;
        UpdateAvailability();
    }

    public void SetTestState(bool isRunning, string message)
    {
        TestConnectionButton.IsEnabled = !isRunning
            && !_isOffline
            && EnabledSwitch.IsOn;
        TestConnectionButton.Content = isRunning ? "正在测试…" : "测试 Steam 连接";
        TestStatusText.Text = message;
    }

    private void EnabledSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        UpdateAvailability();
        NotifyChanged();
    }

    private void Field_LostFocus(object sender, RoutedEventArgs e)
    {
        _credentialSaveTimer.Stop();
        NotifyChanged();
    }

    private void SteamIdBox_TextChanged(object sender, TextChangedEventArgs e) =>
        ScheduleCredentialSave();

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (!_isInitializing)
        {
            _apiKeyChanged = true;
            ScheduleCredentialSave();
        }
    }

    private void ScheduleCredentialSave()
    {
        if (_isInitializing)
        {
            return;
        }
        _credentialSaveTimer.Stop();
        _credentialSaveTimer.Start();
    }

    private void TestConnectionButton_Click(object sender, RoutedEventArgs e) =>
        TestConnectionRequested?.Invoke(this, EventArgs.Empty);

    private void OpenApiKeyPage_Click(object sender, RoutedEventArgs e) =>
        OpenApiKeyPageRequested?.Invoke(this, EventArgs.Empty);

    private void UpdateAvailability()
    {
        var enabled = EnabledSwitch.IsOn;
        CredentialsPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        EnabledStateText.Text = enabled ? "开" : "关";
        TestConnectionButton.IsEnabled = enabled && !_isOffline;
        if (_isOffline)
        {
            TestStatusText.Text = "离线模式下不会访问 Steam Web API。";
        }
        else if (!enabled)
        {
            TestStatusText.Text = "启用游戏库补全后可测试凭据。";
        }
        else if (TestStatusText.Text is
                 "离线模式下不会访问 Steam Web API。"
                 or "启用游戏库补全后可测试凭据。")
        {
            TestStatusText.Text = "验证凭据并读取账号拥有的游戏数量。";
        }
    }

    private void NotifyChanged()
    {
        if (!_isInitializing)
        {
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
