using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Satli_Gui.Services;

internal static class WindowMaterialService
{
    public static string Normalize(string? material) => material switch
    {
        "acrylic" => "acrylic",
        "solid" => "solid",
        _ => "mica",
    };

    public static bool Apply(Window window, string? material)
    {
        try
        {
            window.SystemBackdrop = Normalize(material) switch
            {
                "acrylic" => new DesktopAcrylicBackdrop(),
                "solid" => null,
                _ => new MicaBackdrop(),
            };
            return true;
        }
        catch
        {
            window.SystemBackdrop = null;
            return false;
        }
    }
}
