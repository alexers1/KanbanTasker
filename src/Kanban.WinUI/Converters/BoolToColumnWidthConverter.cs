using System;
using Microsoft.UI.Xaml.Data;

namespace Kanban_WinUI.Converters
{
    /// <summary>Column width: narrow strip when collapsed, full width otherwise.</summary>
    public class BoolToColumnWidthConverter : IValueConverter
    {
        public double CollapsedWidth { get; set; } = 56;
        public double ExpandedWidth { get; set; } = 320;

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return value is bool b && b ? CollapsedWidth : ExpandedWidth;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotSupportedException();
        }
    }
}
