using System;
using System.Threading.Tasks;
using KanbanTasker.Services;
using Microsoft.UI.Xaml;

namespace Kanban_WinUI.Views
{
    /// <summary>
    /// First-run / recovery window: lets the user enter SQL Server connection details,
    /// verifies them, and writes appsettings.json on success.
    /// </summary>
    public sealed partial class DatabaseConfigWindow : Window
    {
        /// <summary>Raised after a verified configuration has been saved.</summary>
        public event EventHandler<DatabaseConfig>? ConfigSaved;

        private bool _saved;

        public DatabaseConfigWindow(DatabaseConfig? initial, string? initialError)
        {
            InitializeComponent();
            AppWindow.Resize(new Windows.Graphics.SizeInt32(560, 720));

            var cfg = initial ?? new DatabaseConfig();
            ServerBox.Text = cfg.Server;
            DatabaseBox.Text = cfg.Database;
            WindowsAuthToggle.IsOn = cfg.IntegratedSecurity;
            UserBox.Text = cfg.UserId;
            PasswordBox.Password = cfg.Password;
            UpdateAuthFields();

            if (!string.IsNullOrEmpty(initialError))
                StatusText.Text = "Could not connect with the saved settings: " + initialError;

            // Closing without a working configuration means the app cannot run
            Closed += (_, _) =>
            {
                if (!_saved)
                    Application.Current.Exit();
            };
        }

        private void WindowsAuthToggle_Toggled(object sender, RoutedEventArgs e) => UpdateAuthFields();

        private void UpdateAuthFields()
        {
            bool sqlAuth = !WindowsAuthToggle.IsOn;
            UserBox.IsEnabled = sqlAuth;
            PasswordBox.IsEnabled = sqlAuth;
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            var cfg = new DatabaseConfig
            {
                Server = string.IsNullOrWhiteSpace(ServerBox.Text) ? @".\SQLEXPRESS" : ServerBox.Text.Trim(),
                Database = string.IsNullOrWhiteSpace(DatabaseBox.Text) ? "KanbanTasker" : DatabaseBox.Text.Trim(),
                IntegratedSecurity = WindowsAuthToggle.IsOn,
                UserId = UserBox.Text.Trim(),
                Password = PasswordBox.Password
            };

            ConnectButton.IsEnabled = false;
            BusyRing.IsActive = true;
            StatusText.Text = "Connecting…";

            string? error = await Task.Run(cfg.TestConnection);

            BusyRing.IsActive = false;
            ConnectButton.IsEnabled = true;

            if (error != null)
            {
                StatusText.Text = "Connection failed: " + error;
                return;
            }

            try
            {
                cfg.Save();
            }
            catch (Exception saveEx)
            {
                StatusText.Text = "Connected, but saving appsettings.json failed: " + saveEx.Message;
                return;
            }

            StatusText.Text = "Connected — starting Kanban Tasker…";
            _saved = true;
            ConfigSaved?.Invoke(this, cfg);
        }
    }
}
