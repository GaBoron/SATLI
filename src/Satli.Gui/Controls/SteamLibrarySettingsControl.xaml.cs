using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satli_Gui.Models;

namespace Satli_Gui.Controls;

public sealed partial class SteamLibrarySettingsControl : UserControl
{
    private bool _isInitializing;
    private bool _isOffline;

    public event EventHandler? SettingsChanged;
    public event EventHandler? TestConnectionRequested;
    public event EventHandler? OpenApiKeyPageRequested;

    public SteamLibrarySettingsControl()
    {
        InitializeComponent();
    }

    public void LoadSettings(SteamLibrarySettings settings, bool isOffline)
    {
        _isInitializing = true;
        EnabledSwitch.IsOn = settings.Enabled;
        SteamIdBox.Text = settings.SteamId;
        ApiKeyBox.Password = settings.ApiKey;
        _isOffline = isOffline;
        UpdateAvailability();
        _isInitializing = false;
    }

    public SteamLibrarySettings ReadSettings() => new()
    {
        Enabled = EnabledSwitch.IsOn,
        SteamId = SteamIdBox.Text,
        ApiKey = ApiKeyBox.Password,
    };

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

    private void Field_LostFocus(object sender, RoutedEventArgs e) => NotifyChanged();

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
