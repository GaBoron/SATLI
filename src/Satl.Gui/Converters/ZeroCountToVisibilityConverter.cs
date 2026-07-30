using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Satl_Gui.Converters;

public sealed class ZeroCountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
