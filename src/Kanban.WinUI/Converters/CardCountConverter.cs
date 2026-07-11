using System;
using Microsoft.UI.Xaml.Data;
using Kanban_WinUI.Models;

namespace Kanban_WinUI.Converters
{
    public class CardCountConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (parameter != null && parameter.ToString() == "SwimlaneCardCount")
            {
                int itemsCount = (int)value;
                if (itemsCount == 0 || itemsCount == 1)
                {
                    return value + " " + "Item";
                }
                return value + " " + "Items";
            }

            PresentationColumn column = value as PresentationColumn;
            if (column == null)
            {
                return string.Empty;
            }

            if (column.MaxTaskLimit > 0)
            {
                return "  |  " + "Max:" + " " + column.MaxTaskLimit;
            }
            else
            {
                return string.Empty;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            return value;
        }
    }
}