using Kanban_WinUI.Services.Api;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Kanban_WinUI.Views.Dialogs
{
    /// <summary>
    /// Shown at startup when the LAN API can't be reached. Lists the error (copyable) and lets
    /// the user correct the server IP/port; on Retry the change is saved to <see cref="ApiConfig"/>.
    /// </summary>
    public sealed partial class ConnectionErrorDialog : ContentDialog
    {
        private readonly ApiConfig _config;

        public ConnectionErrorDialog(ApiConfig config, string error)
        {
            InitializeComponent();
            _config = config;

            ErrorBox.Text = string.IsNullOrWhiteSpace(error) ? "(no details)" : error;
            IpBox.Text = config.ServerIp;
            PortBox.Text = config.ServerPort.ToString();
            UpdatePreview();

            // On Retry, write the entered IP/port back into the config and persist it.
            PrimaryButtonClick += (s, e) =>
            {
                _config.ServerIp = string.IsNullOrWhiteSpace(IpBox.Text) ? "192.168.5.2" : IpBox.Text.Trim();
                _config.ServerPort = int.TryParse(PortBox.Text?.Trim(), out var p) ? p : 5001;
                _config.Save();
            };
        }

        private void Field_Changed(object sender, TextChangedEventArgs e) => UpdatePreview();

        private void UpdatePreview()
        {
            if (Preview == null) return;
            int port = int.TryParse(PortBox.Text?.Trim(), out var p) ? p : 5001;
            string ip = string.IsNullOrWhiteSpace(IpBox.Text) ? "192.168.5.2" : IpBox.Text.Trim();
            Preview.Text = $"Will connect to: http://{ip}:{port}";
        }

        private void Copy_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            var data = new DataPackage();
            data.SetText(ErrorBox.Text);
            Clipboard.SetContent(data);
        }
    }
}
