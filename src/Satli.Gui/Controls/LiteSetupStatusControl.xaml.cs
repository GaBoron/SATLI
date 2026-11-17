using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Satli.Core.SatliLite;
using Satli_Gui.Services;

namespace Satli_Gui.Controls;

public sealed partial class LiteSetupStatusControl : UserControl
{
    public LiteSetupStatusControl() => InitializeComponent();

    public void Update(LiteSetupStatus status)
    {
        var display = LiteSetupStatusDisplay.From(status);
        SetValue(MillenniumValue, display.Millennium, status.MillenniumInstalled);
        SetValue(InstalledValue, display.Installed, status.PluginInstalled);
        SetValue(EnabledValue, display.Enabled, status.PluginEnabled == true);
        SetValue(ConnectionValue, display.Connection, status.Connection.Connected);
        NextStepText.Text = display.NextStep;
    }

    private void SetValue(TextBlock target, string text, bool complete)
    {
        target.Text = (complete ? "✓ " : "○ ") + text;
        target.Style = (Style)Resources[complete ? "CompleteStatusStyle" : "PendingStatusStyle"];
    }
}
