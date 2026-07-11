using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Kanban_WinUI.Converters
{
    /// <summary>
    /// Collapses the element when the bound value is null (or an empty/whitespace string).
    /// Used for optional card fields such as description, tags and due date.
    /// </summary>
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool visible = value switch
            {
                null => false,
                string s => !string.IsNullOrWhiteSpace(s),
                _ => true
            };
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Formats a nullable DateTimeOffset as a short date, e.g. "Jul 17".
    /// </summary>
    public class DateToShortDateConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is DateTimeOffset d)
                return d.ToString("MMM d");
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }
}
