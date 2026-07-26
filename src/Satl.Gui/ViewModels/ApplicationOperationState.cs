using CommunityToolkit.Mvvm.ComponentModel;

namespace Satl_Gui.ViewModels;

public sealed class ApplicationOperationState : ObservableObject
{
    private bool _isBusy;
    private string _statusMessage = "准备就绪";

    public GameLoadingProgress GameLoading { get; } = new();

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool TryBegin()
    {
        if (IsBusy)
        {
            return false;
        }
        IsBusy = true;
        return true;
    }

    public void SetStatus(string message) => StatusMessage = message;

    public void Complete()
    {
        StatusMessage = "准备就绪";
        IsBusy = false;
    }
}
