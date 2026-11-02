using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satli.Core.Formats;
using Satli.Core.TranslationProjects;
using Satli_Gui.Services;
using Satli_Gui.Controls;

namespace Satli_Gui.Pages;

public sealed partial class AchievementEditorPage
{
    private sealed record AiPreviewItem(string ApiName, AchievementTranslation Value, string Text);

    private async void AiTranslate_Click(object sender, RoutedEventArgs e)
    {
        if (_inspection is null || _isBusy) return;
        if (!await SaveDraftCheckpointAsync()) return;
        _isBusy = true;
        try
        {
            var store = new AiProposalStore(_inspection, _targetLanguage);
            var proposals = await store.LoadAsync();
            var references = (await store.LoadReferencesAsync()).ToDictionary(item => item.Url, StringComparer.Ordinal);
            var sources = _inspection.Rows.Select(row => new AchievementPreviewRow(row.Index, row.ApiName,
                row.Translations.ToDictionary(pair => pair.Key,
                    pair => new AchievementTranslation(pair.Value.Name, pair.Value.Description), StringComparer.Ordinal))).ToArray();
            var sourceById = sources.ToDictionary(row => row.ApiName, StringComparer.Ordinal);
            // Revalidate cached results before preview. Corrupt proposals never overwrite manual edits.
            foreach (var pair in proposals.ToArray())
            {
                sourceById.TryGetValue(pair.Key, out var original);
                try
                {
                    if (original is null) throw new InvalidDataException();
                    if (_project is null && (pair.Value.Name.Any(char.IsControl) || pair.Value.Description.Any(char.IsControl)))
                        throw new InvalidDataException();
                    var texts = original.Translations.Where(item => item.Key != _targetLanguage).Select(item => item.Value).ToArray();
                    TranslationTextValidation.Validate(pair.Key, pair.Value, texts.Length == 0 ? original.Translations.Values : texts);
                }
                catch { proposals.Remove(pair.Key); }
            }
            var retryOnly = new CheckBox { Content = "仅生成尚无有效待审结果的项目", IsChecked = proposals.Count > 0 };
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock
            {
                Text = $"目标语言：{_targetLanguage}；成就：{sources.Length}；已有待审结果：{proposals.Count}。\n"
                    + "将向设置中的服务发送游戏名、App ID、API ID 和所有实际可用语言文本，可能产生费用。生成结果仅供人工校对。",
                TextWrapping = TextWrapping.Wrap,
            });
            content.Children.Add(retryOnly);
            var choice = new ContentDialog
            {
                XamlRoot = XamlRoot, Title = "AI 翻译", Content = content,
                PrimaryButtonText = "生成参考译文", SecondaryButtonText = proposals.Count > 0 ? "查看待审结果" : "",
                CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close,
            };
            var selected = await choice.ShowAsync();
            if (selected == ContentDialogResult.None) return;
            var errors = new Dictionary<string, string>();
            if (selected == ContentDialogResult.Primary)
            {
                if (App.ViewModel.Settings.Offline) throw new InvalidOperationException();
                var options = AiTranslationSettings.Options(App.ViewModel.Settings.Ai);
                var key = AiTranslationSettings.Key(App.ViewModel.Settings.Ai);
                AiTranslationClient.ValidateOptions(options, key);
                var requested = sources.Where(row => retryOnly.IsChecked != true || !proposals.ContainsKey(row.ApiName)).ToArray();
                if (requested.Length > 0)
                {
                    using var cancel = new CancellationTokenSource();
                    using var progressContent = new AiGenerationProgress(requested.Length);
                    var progressDialog = new ContentDialog
                    {
                        XamlRoot = XamlRoot, Title = "正在生成 AI 参考译文", Content = progressContent,
                        CloseButtonText = "取消并保留已完成结果",
                    };
                    progressDialog.CloseButtonClick += (_, args) => { args.Cancel = true; cancel.Cancel(); };
                    var visible = progressDialog.ShowAsync();
                    await App.Logs.WriteAsync("信息", "AI 翻译", $"开始生成。App ID={_inspection.AppId}；目标语言={_targetLanguage}；项目={requested.Length}。");
                    try
                    {
                        using var client = NetworkHttpClientFactory.Create(App.ViewModel.Settings.Network, allowAutoRedirect: false);
                        await Task.Run(() => new AiTranslationClient(client).TranslateAsync(options, key,
                            _inspection.AppId, _inspection.GameName, _targetLanguage, requested,
                            async (result, progress) =>
                            {
                                foreach (var pair in result.Translations) proposals[pair.Key] = pair.Value;
                                foreach (var reference in result.References ?? []) references[reference.Url] = reference;
                                errors = result.Errors.ToDictionary(pair => pair.Key, pair => pair.Value);
                                await store.SaveAsync(proposals, references.Values.ToArray());
                                progressContent.Batch(progress);
                            }, cancel.Token, progressContent.Activity));
                        await App.Logs.WriteAsync("信息", "AI 翻译", $"生成结束；有效待审={proposals.Count}；失败={errors.Count}。");
                    }
                    catch (OperationCanceledException)
                    {
                        await App.Logs.WriteAsync("信息", "AI 翻译", $"生成已取消；保留待审结果={proposals.Count}。");
                    }
                    finally { progressDialog.Hide(); await visible; }
                }
            }
            foreach (var pair in proposals.ToArray())
            {
                if (_project is null && (pair.Value.Name.Any(char.IsControl) || pair.Value.Description.Any(char.IsControl)))
                {
                    proposals.Remove(pair.Key);
                    errors[pair.Key] = "BIN 文本不能包含换行或控制字符，请重试此项。";
                }
            }
            await store.SaveAsync(proposals, references.Values.ToArray());
            await PreviewAiAsync(proposals, errors, references.Values.ToArray());
        }
        catch
        {
            App.ViewModel.ShowInfo("AI 翻译未完成。请检查 AI 设置、模型权限及网络；已保存的人工草稿和待审结果保留。", InfoBarSeverity.Warning);
            await App.Logs.WriteAsync("警告", "AI 翻译", "生成或预览未完成；未记录请求、回复或密钥。");
        }
        finally
        {
            _isBusy = false;
            if (HasUnsavedChanges) _autosave.Start();
        }
    }

    private async Task PreviewAiAsync(Dictionary<string, AchievementTranslation> proposals, Dictionary<string, string> errors,
        IReadOnlyList<AiReference> references)
    {
        var list = new ListView { SelectionMode = ListViewSelectionMode.Multiple, Height = 360, MaxWidth = 680,
            ItemTemplate = (DataTemplate)Resources["AiPreviewTemplate"] };
        list.ItemsSource = _inspection!.Rows.Where(row => proposals.ContainsKey(row.ApiName)).Select(row =>
        {
            var value = proposals[row.ApiName];
            return new AiPreviewItem(row.ApiName, value,
                $"{row.ApiName}\n名称：{row.TargetName} → {value.Name}\n说明：{row.TargetDescription} → {value.Description}");
        }).ToArray();
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = $"有效待审 {proposals.Count} 项；本轮失败 {errors.Count} 项。AI 生成不等于已审校。\n"
                + "“填充空白”逐字段保留已有文本；“覆盖选中项”只替换下方选中的成就。取消会保留待审结果。",
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(list);
        if (errors.Count > 0) content.Children.Add(new TextBlock { TextWrapping = TextWrapping.Wrap,
            Text = string.Join("\n", errors.Take(5).Select(pair => $"{pair.Key}：{pair.Value}")) + "\n再次点击 AI 翻译可重试缺少项目。" });
        var referenceList = new StackPanel { Spacing = 4 };
        referenceList.Children.Add(new TextBlock { Text = "参考网站", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        if (references.Count == 0) referenceList.Children.Add(new TextBlock
        {
            Text = "服务未返回可核验的网站来源。", TextWrapping = TextWrapping.Wrap,
        });
        foreach (var reference in references)
        {
            var link = new HyperlinkButton
            {
                Content = new TextBlock { Text = $"{reference.Title} · {new Uri(reference.Url).Host}",
                    TextWrapping = TextWrapping.Wrap },
                NavigateUri = new Uri(reference.Url), HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(0, 4, 0, 4),
            };
            ToolTipService.SetToolTip(link, reference.Url);
            referenceList.Children.Add(link);
        }
        content.Children.Add(new ScrollViewer { Content = referenceList, MaxHeight = 130,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "审阅 AI 参考译文", Content = content,
            PrimaryButtonText = proposals.Count > 0 ? "填充空白" : "",
            SecondaryButtonText = proposals.Count > 0 ? "覆盖选中项" : "", CloseButtonText = "保留待审，暂不填充",
            DefaultButton = ContentDialogButton.Close,
        };
        dialog.SecondaryButtonClick += (_, args) => { if (list.SelectedItems.Count == 0) args.Cancel = true; };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None) return;
        var selected = list.SelectedItems.Cast<AiPreviewItem>().Select(item => item.ApiName).ToHashSet(StringComparer.Ordinal);
        var count = 0;
        _changingLanguage = true;
        try
        {
            foreach (var row in _inspection.Rows)
            {
                if (!proposals.TryGetValue(row.ApiName, out var value)) continue;
                if (result == ContentDialogResult.Secondary && !selected.Contains(row.ApiName)) continue;
                if (result == ContentDialogResult.Secondary || string.IsNullOrWhiteSpace(row.TargetName)) row.TargetName = value.Name;
                if (result == ContentDialogResult.Secondary || string.IsNullOrWhiteSpace(row.TargetDescription)) row.TargetDescription = value.Description;
                count++;
            }
        }
        finally { _changingLanguage = false; }
        await App.Logs.WriteAsync("信息", "AI 翻译", $"用户接受参考译文；项目={count}；覆盖选中={result == ContentDialogResult.Secondary}。");
        // Save accepted text through the same draft path used by manual edits.
        _isBusy = false;
        await SaveDraftCheckpointAsync(recordHistory: false);
        _isBusy = true;
        UpdateStatus();
    }
}
