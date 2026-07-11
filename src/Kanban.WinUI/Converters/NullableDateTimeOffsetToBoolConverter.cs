using System;
using Microsoft.UI.Xaml.Data;

namespace Kanban_WinUI.Converters
{
    public class NullableDateTimeOffsetToBoolConverter : IValueConverter 
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value == null)
                return false;

            DateTimeOffset? dateTime = value as DateTimeOffset?;
            if (dateTime == null)
                return false;

            return dateTime.HasValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}