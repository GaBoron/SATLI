using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satli.Core.Formats;
using Satli.Core.TranslationProjects;
using Satli_Gui.Models;
using Satli_Gui.Services;
using AchievementTranslation = Satli.Core.Formats.AchievementTranslation;

namespace Satli_Gui.Controls;

public sealed partial class AiSettingsControl : UserControl
{
    private AiSettings _settings = new();
    private bool _loading;
    public event EventHandler? SettingsChanged;

    public AiSettingsControl() { InitializeComponent(); }

    public void LoadSettings(AiSettings settings)
    {
        _loading = true;
        _settings = settings;
        ProtocolBox.SelectedIndex = settings.Protocol switch { "chat" => 1, "anthropic" => 2, _ => 0 };
        EndpointBox.Text = settings.Endpoint;
        ModelBox.Text = settings.Model;
        StructuredBox.IsChecked = settings.StructuredOutput;
        SearchBox.IsChecked = settings.WebSearch;
        KeyBox.Password = "";
        UpdateKeyStatus();
        _loading = false;
    }

    public AiSettings ReadSettings() => _settings;

    private void Protocol_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || EndpointBox is null) return;
        EndpointBox.Text = ProtocolBox.SelectedIndex switch
        {
            1 => "https://api.openai.com/v1/chat/completions",
            2 => "https://api.anthropic.com/v1/messages",
            _ => "https://api.openai.com/v1/responses",
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var protocol = (ProtocolBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "responses";
            var endpoint = EndpointBox.Text.Trim();
            var key = KeyBox.Password;
            var protectedKey = key.Length > 0 ? AiTranslationSettings.Protect(key)
                : protocol == _settings.Protocol && endpoint == _settings.Endpoint ? _settings.ProtectedApiKey : "";
            _settings = new AiSettings
            {
                Protocol = protocol, Endpoint = endpoint, Model = ModelBox.Text.Trim(), ProtectedApiKey = protectedKey,
                StructuredOutput = StructuredBox.IsChecked == true, WebSearch = SearchBox.IsChecked == true && protocol != "chat",
            };
            KeyBox.Password = "";
            UpdateKeyStatus();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch
        {
            StatusText.Text = "无法加密密钥，设置未保存。请检查当前 Windows 用户环境。";
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _settings.ProtectedApiKey = "";
        KeyBox.Password = "";
        UpdateKeyStatus();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ReportSaved(bool success) => StatusText.Text = success ? "AI 设置已保存。" : "AI 设置保存失败，请重试。";

    private void UpdateKeyStatus() => KeyStatus.Text = _settings.ProtectedApiKey.Length == 0
        ? "尚未保存密钥。" : "密钥已在本机加密保存；可使用“清除密钥”移除。";

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (App.ViewModel.Settings.Offline) { StatusText.Text = "离线模式下不能测试 AI 服务。"; return; }
        TestButton.IsEnabled = false;
        await App.Logs.WriteAsync("信息", "AI 翻译", "开始测试已保存的 AI 配置。");
        try
        {
            using var client = NetworkHttpClientFactory.Create(App.ViewModel.Settings.Network, allowAutoRedirect: false);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(130));
            var result = await new AiTranslationClient(client).TranslateAsync(AiTranslationSettings.Options(_settings),
                AiTranslationSettings.Key(_settings), "0", "Connection test", "schinese",
                [new(1, "CONNECTION_TEST", new Dictionary<string, AchievementTranslation> { ["english"] = new("Test", "") })],
                null, cancellation.Token);
            StatusText.Text = result.Translations.Count == 1 ? "模型连接和结构化结果校验通过。"
                : "服务未返回有效译文。请检查模型、接口、权限或额度。";
            await App.Logs.WriteAsync("信息", "AI 翻译", $"模型测试完成；通过={result.Translations.Count == 1}。");
        }
        catch
        {
            StatusText.Text = "测试失败，请检查已保存的接口、模型、密钥和网络设置。";
            await App.Logs.WriteAsync("警告", "AI 翻译", "模型测试失败。未记录请求、回复或密钥。");
        }
        finally { TestButton.IsEnabled = true; }
    }
}
