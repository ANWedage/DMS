using System.Windows;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using DMS.Data;
using DMS.Helpers;
using DMS.Models;
using DMS.Services;
using DMS.Views;
using MongoDB.Driver;

namespace DMS
{
    public partial class App : Application
    {
        private const string CurrentVersion = "1.0.0";
        private const string LatestReleaseApiUrl = "https://api.github.com/repos/ANWedage/DMS/releases/latest";

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            MongoDbContext? context = null;
            try
            {
                context = new MongoDbContext();
                context.EnsureIndexes();
                SeedAdminUsers(context);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"DMS could not reach its database. The app will continue in offline mode, but account actions will be unavailable until the MongoDB connection is restored.\n\nDetails: {ex.Message}",
                    "Database connection unavailable",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            context ??= new MongoDbContext();
            IUserService userService = new UserService(context);

            var loginWindow = new LoginWindow(userService);
            loginWindow.Show();
            _ = CheckForUpdatesAsync();
        }

        private static async Task CheckForUpdatesAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("DMS-Update-Checker/1.0");
                using var response = await client.GetAsync(LatestReleaseApiUrl);
                if (!response.IsSuccessStatusCode)
                    return;

                await using var responseStream = await response.Content.ReadAsStreamAsync();
                using var release = await JsonDocument.ParseAsync(responseStream);
                var root = release.RootElement;
                var tagName = root.GetProperty("tag_name").GetString()?.TrimStart('v');
                var releaseUrl = root.GetProperty("html_url").GetString();
                var downloadUrl = FindZipAssetUrl(root);

                if (!Version.TryParse(tagName, out var latestVersion)
                    || !Version.TryParse(CurrentVersion, out var currentVersion)
                    || latestVersion <= currentVersion
                    || string.IsNullOrWhiteSpace(releaseUrl))
                    return;

                Current.Dispatcher.Invoke(() =>
                {
                    var result = MessageBox.Show(
                        $"A new DMS update is available (version {latestVersion}). Download and install it now?",
                        "DMS update available",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        if (string.IsNullOrWhiteSpace(downloadUrl))
                            Process.Start(new ProcessStartInfo(releaseUrl) { UseShellExecute = true });
                        else
                            _ = DownloadAndInstallUpdateAsync(downloadUrl, latestVersion.ToString());
                    }
                });
            }
            catch
            {
                // Update checks are optional and must not prevent login when offline.
            }
        }

        private static string? FindZipAssetUrl(JsonElement release)
        {
            if (!release.TryGetProperty("assets", out var assets))
                return null;

            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                if (name?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true)
                    return asset.GetProperty("browser_download_url").GetString();
            }

            return null;
        }

        private static async Task DownloadAndInstallUpdateAsync(string downloadUrl, string version)
        {
            try
            {
                var currentExecutable = Environment.ProcessPath;
                var installDirectory = Path.GetDirectoryName(currentExecutable);
                if (string.IsNullOrWhiteSpace(currentExecutable) || string.IsNullOrWhiteSpace(installDirectory))
                    throw new InvalidOperationException("The current application path could not be determined.");

                var temporaryDirectory = Path.Combine(Path.GetTempPath(), "DMS-update");
                Directory.CreateDirectory(temporaryDirectory);
                var zipPath = Path.Combine(temporaryDirectory, $"DMS-{version}.zip");
                var scriptPath = Path.Combine(temporaryDirectory, "install-update.cmd");

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("DMS-Update-Installer/1.0");
                using var downloadStream = await client.GetStreamAsync(downloadUrl);
                using var zipStream = File.Create(zipPath);
                await downloadStream.CopyToAsync(zipStream);

                var escapedZipPath = zipPath.Replace("'", "''");
                var escapedInstallDirectory = installDirectory.Replace("'", "''");
                var escapedExecutable = currentExecutable.Replace("'", "''");
                var script = $"@echo off\r\ntimeout /t 2 /nobreak >nul\r\npowershell -NoProfile -ExecutionPolicy Bypass -Command \"Expand-Archive -LiteralPath '{escapedZipPath}' -DestinationPath '{escapedInstallDirectory}' -Force\"\r\nstart \"\" \"{escapedExecutable}\"\r\ndel \"%~f0\"\r\n";
                await File.WriteAllTextAsync(scriptPath, script);

                Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(
                        "The update has been downloaded. DMS will close, install the update, and restart.",
                        "Installing DMS update",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    Process.Start(new ProcessStartInfo(scriptPath) { UseShellExecute = true, WorkingDirectory = temporaryDirectory });
                    Current.Shutdown();
                });
            }
            catch (Exception ex)
            {
                Current.Dispatcher.Invoke(() => MessageBox.Show(
                    $"The update could not be installed automatically. Please download it from GitHub.\n\nDetails: {ex.Message}",
                    "DMS update",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning));
            }
        }

        private static void SeedAdminUsers(MongoDbContext context)
        {
            var configuredAdmins = AdminConfig.GetConfiguredAdmins();

            foreach (var admin in configuredAdmins)
            {
                var existing = context.Admins.Find(a => a.Username == admin.Username).FirstOrDefault();
                if (existing != null)
                    continue;

                var (hash, salt) = PasswordHasher.HashPassword(admin.Password);
                context.Admins.InsertOne(new AdminUser
                {
                    Name = admin.Name,
                    Username = admin.Username,
                    PasswordHash = hash,
                    PasswordSalt = salt
                });
            }
        }
    }
}
