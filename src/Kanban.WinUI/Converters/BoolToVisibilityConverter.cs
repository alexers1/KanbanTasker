using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace Kanban_WinUI.Converters
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool isTrue = value is bool b && b;
            bool inverse = parameter is string p && (p.Equals("Inverse", StringComparison.OrdinalIgnoreCase) || p == "!");
            if (inverse)
                return isTrue ? Visibility.Collapsed : Visibility.Visible;
            return isTrue ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return value is Visibility v && v == Visibility.Visible;
        }
    }
}