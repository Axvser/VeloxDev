using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace Demo.Views;

public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isVisible = value is bool visible && visible;
        var invert = parameter is string text && text.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        if (invert)
        {
            isVisible = !isVisible;
        }

        return isVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        var isVisible = value is Visibility visibility && visibility == Visibility.Visible;
        var invert = parameter is string text && text.Equals("Invert", StringComparison.OrdinalIgnoreCase);
        return invert ? !isVisible : isVisible;
    }
}
