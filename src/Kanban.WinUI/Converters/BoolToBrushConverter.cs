using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Kanban_WinUI.Converters
{
    public class BoolToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            // true = alert state (passed due / over WIP limit) -> red; otherwise a neutral theme brush.
            // Pass ConverterParameter=Accent for an accent-colored (instead of gray) normal state.
            bool isAlert = value is bool b && b;
            if (isAlert)
                return new SolidColorBrush(Microsoft.UI.Colors.Red);

            string fallbackKey = parameter is string p && p.Equals("Accent", StringComparison.OrdinalIgnoreCase)
                ? "AccentTextFillColorPrimaryBrush"
                : "TextFillColorSecondaryBrush";
            try
            {
                if (Application.Current.Resources[fallbackKey] is Brush brush)
                    return brush;
            }
            catch { }
            return new SolidColorBrush(Microsoft.UI.Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotSupportedException();
            //return Application.Current.Resources["RegionBrush"] as AcrylicBrush;
        }
    }
}