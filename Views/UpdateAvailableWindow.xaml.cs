using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace DMS.Views
{
    public partial class UpdateAvailableWindow : Window
    {
        private readonly string _downloadUrl;
        private readonly string _version;
        private readonly string _releaseNotes;

        private bool _isDownloading;
        private bool _closeAfterInstall;

        public UpdateAvailableWindow(string version, string releaseNotes, string downloadUrl)
        {
            InitializeComponent();
            _version = version;
            _releaseNotes = releaseNotes;
            _downloadUrl = downloadUrl;

            VersionTextBlock.Text = $"Version {version}";
            ReleaseNotesTextBlock.Text = FormatReleaseNotes(releaseNotes);
            DownloadProgressBar.Value = 0;
            ProgressStatusText.Text = "Ready to update now.";
            UpdateButton.Content = "Update Now";
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isDownloading)
                return;

            _isDownloading = true;
            UpdateButton.IsEnabled = false;
            ProgressStatusText.Text = "Preparing update...";
            DownloadProgressBar.Value = 0;

            try
            {
                await DownloadAndInstallAsync();
            }
            catch (Exception ex)
            {
                _isDownloading = false;
                UpdateButton.IsEnabled = true;
                ProgressStatusText.Text = $"Download failed: {ex.Message}";
                MessageBox.Show(
                    $"The update could not be downloaded automatically.\n\nDetails: {ex.Message}",
                    "DMS update",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static string FormatReleaseNotes(string? notes)
        {
            if (string.IsNullOrWhiteSpace(notes))
                return "No release notes were provided for this update.";

            var formatted = notes.Replace("\r\n", "\n");
            formatted = Regex.Replace(formatted, @"\[(.+?)\]\((.+?)\)", "$1 ($2)");
            formatted = Regex.Replace(formatted, @"^#+\s*", string.Empty, RegexOptions.Multiline);
            formatted = Regex.Replace(formatted, @"\*\*(.+?)\*\*", "$1");
            formatted = Regex.Replace(formatted, @"\*(.+?)\*", "$1");
            formatted = Regex.Replace(formatted, @"`([^`]*)`", "$1");
            formatted = Regex.Replace(formatted, @"^\s*[-*]\s+", "• ", RegexOptions.Multiline);
            formatted = Regex.Replace(formatted, @"\n{3,}", "\n\n");

            return formatted.Trim();
        }

        private async Task DownloadAndInstallAsync()
        {
            var temporaryDirectory = Path.Combine(Path.GetTempPath(), "DMS-update");
            Directory.CreateDirectory(temporaryDirectory);
            var installerPath = Path.Combine(temporaryDirectory, $"DMS-Setup-{_version}-{Guid.NewGuid():N}.exe");

            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DMS-Update-Installer/1.0");

            using var response = await client.GetAsync(_downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var responseStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            var downloadedBytes = 0L;
            var buffer = new byte[81920];

            while (true)
            {
                var read = await responseStream.ReadAsync(buffer.AsMemory(0, buffer.Length));
                if (read == 0)
                    break;

                await fileStream.WriteAsync(buffer.AsMemory(0, read));
                downloadedBytes += read;

                if (totalBytes > 0)
                {
                    var percentage = (int)((downloadedBytes * 100d) / totalBytes);
                    DownloadProgressBar.Value = Math.Min(100, percentage);
                    ProgressStatusText.Text = $"Downloading update... {percentage}%";
                }
                else
                {
                    ProgressStatusText.Text = $"Downloading update... {downloadedBytes:N0} bytes";
                }
            }

            await fileStream.FlushAsync();
            await fileStream.DisposeAsync();

            DownloadProgressBar.Value = 100;
            ProgressStatusText.Text = "Download complete. Launching installer...";

            var installerProcess = new ProcessStartInfo(installerPath)
            {
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(installerProcess);
            _closeAfterInstall = true;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            if (_closeAfterInstall)
            {
                Application.Current?.Shutdown();
            }
        }
    }
}
