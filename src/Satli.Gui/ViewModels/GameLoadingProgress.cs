using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Satli_Gui.Models;

namespace Satli_Gui.ViewModels;

public sealed class GameLoadingProgress : ObservableObject
{
    private bool _isActive;
    private bool _isIndeterminate;
    private double _value;
    private double _maximum = 1;
    private string _text = string.Empty;

    public bool IsActive
    {
        get => _isActive;
        private set
        {
            if (SetProperty(ref _isActive, value))
            {
                OnPropertyChanged(nameof(Visibility));
            }
        }
    }

    public Visibility Visibility => IsActive
        ? Microsoft.UI.Xaml.Visibility.Visible
        : Microsoft.UI.Xaml.Visibility.Collapsed;

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        private set => SetProperty(ref _isIndeterminate, value);
    }

    public double Value
    {
        get => _value;
        private set => SetProperty(ref _value, value);
    }

    public double Maximum
    {
        get => _maximum;
        private set => SetProperty(ref _maximum, value);
    }

    public string Text
    {
        get => _text;
        private set => SetProperty(ref _text, value);
    }

    public void Start(string message)
    {
        Maximum = 1;
        Value = 0;
        Text = message;
        IsIndeterminate = true;
        IsActive = true;
    }

    public void Handle(SatliEvent satliEvent)
    {
        if (satliEvent.Event == "plan" && TryReadInt(satliEvent, "count", out var count))
        {
            SetProgress(0, count, count == 0 ? "没有需要加载的游戏" : $"正在加载游戏 0/{count}");
            return;
        }

        if (satliEvent.Event == "progress"
            && TryReadInt(satliEvent, "current", out var current)
            && TryReadInt(satliEvent, "total", out var total))
        {
            var message = satliEvent.Payload.TryGetProperty("message", out var value)
                ? value.GetString() ?? $"正在加载游戏 {current}/{total}"
                : $"正在加载游戏 {current}/{total}";
            SetProgress(current, total, message);
            return;
        }

        if (satliEvent.Event == "item-succeeded")
        {
            var itemPosition = TryReadInt(satliEvent, "position", out var position)
                ? position
                : Math.Min((int)Maximum, (int)Value + 1);
            SetProgress(itemPosition, (int)Maximum, $"正在加载游戏 {itemPosition}/{(int)Maximum}");
        }
    }

    public void Finish(string message)
    {
        IsIndeterminate = false;
        Value = Maximum;
        Text = message;
        IsActive = false;
    }

    public void Fail(string message)
    {
        IsIndeterminate = false;
        Text = message;
        IsActive = false;
    }

    private void SetProgress(int current, int total, string message)
    {
        Maximum = Math.Max(total, 1);
        Value = Math.Clamp(current, 0, (int)Maximum);
        Text = message;
        IsIndeterminate = false;
        IsActive = true;
    }

    private static bool TryReadInt(SatliEvent satliEvent, string property, out int value)
    {
        value = 0;
        return satliEvent.Payload.TryGetProperty(property, out var element)
            && element.TryGetInt32(out value);
    }
}
