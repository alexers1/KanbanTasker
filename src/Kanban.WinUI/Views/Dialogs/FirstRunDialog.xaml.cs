using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kanban_WinUI.Views.Dialogs
{
    public sealed partial class FirstRunDialog : ContentDialog
    {
        public FirstRunDialog()
        {
            this.InitializeComponent();
        }

        private void btnCloseDialog_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
        }
    }
}