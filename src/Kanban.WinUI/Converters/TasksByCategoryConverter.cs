using Microsoft.UI.Xaml.Data;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using Kanban_WinUI.Models;

namespace Kanban_WinUI.Converters
{
    public class TasksByCategoryConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is ObservableCollection<PresentationTask> tasks && parameter is string category)
            {
                return new ObservableCollection<PresentationTask>(tasks.Where(t => t.Category == category));
            }
            return new ObservableCollection<PresentationTask>();
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => null!;
    }
}