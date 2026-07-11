using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;
using Microsoft.UI;

namespace Kanban_WinUI.Converters
{
    public class StringToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var key = value?.ToString()?.ToLower() ?? "low";
            return key switch
            {
                "high" => new SolidColorBrush(Microsoft.UI.Colors.Red),
                "medium" => new SolidColorBrush(Microsoft.UI.Colors.Orange),
                _ => new SolidColorBrush(Microsoft.UI.Colors.Green)
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => null!;
    }
}