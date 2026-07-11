using Microsoft.UI.Xaml.Data;
using System;

namespace Kanban_WinUI.Converters
{
    public class StringToTimeSpanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is string str && TimeSpan.TryParse(str, out var ts))
                return ts;
            return TimeSpan.Zero;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is TimeSpan ts)
                return ts.ToString(@"hh\:mm");
            return "00:00";
        }
    }
}
