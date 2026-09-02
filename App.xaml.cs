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

            IUserService userService;
            var apiBaseUrl = MongoConfig.GetEnvironmentValue("DMS_API_BASE_URL");
            if (!string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                userService = new ApiUserService(apiBaseUrl);
            }
            else
            {
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
                userService = new UserService(context);
            }

            Window startupWindow;
            var savedSession = AppSession.Load();
            if (savedSession == null || string.IsNullOrWhiteSpace(savedSession.UserId))
            {
                startupWindow = new LoginWindow(userService);
            }
            else
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(savedSession.AccessToken))
                        AppSession.SetAccessToken(savedSession.AccessToken);

                    if (string.Equals(savedSession.Role, "Admin", StringComparison.OrdinalIgnoreCase))
                    {
                        AppSession.SetAdmin(savedSession.DisplayName ?? savedSession.Username ?? "Admin", savedSession.Username ?? savedSession.UserId);
                        startupWindow = new AdminWindow(userService);
                    }
                    else
                    {
                        var savedUser = new User
                        {
                            Id = savedSession.UserId,
                            Username = savedSession.Username,
                            Email = savedSession.Username ?? string.Empty
                        };
                        AppSession.SetCurrentUser(savedUser);
                        var currentUser = userService.GetUserById(savedUser.Id);
                        AppSession.SetCurrentUser(currentUser);
                        startupWindow = new MainWindow(currentUser, userService);
                    }
                }
                catch
                {
                    AppSession.Clear();
                    startupWindow = new LoginWindow(userService);
                }
            }

            startupWindow.Show();
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
                var downloadUrl = FindInstallerAssetUrl(root);

                if (!Version.TryParse(tagName, out var latestVersion)
                    || !Version.TryParse(CurrentVersion, out var currentVersion)
                    || latestVersion <= currentVersion
                    || string.IsNullOrWhiteSpace(downloadUrl))
                    return;

                Current.Dispatcher.Invoke(() =>
                {
                    var result = MessageBox.Show(
                        $"A new DMS update (version {latestVersion}) is required. Select OK to download and install it now.",
                        "DMS update available",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.OK)
                    {
                        _ = DownloadAndInstallUpdateAsync(downloadUrl, latestVersion.ToString());
                    }
                });
            }
            catch
            {
                // Update checks are optional and must not prevent login when offline.
            }
        }

        private static string? FindInstallerAssetUrl(JsonElement release)
        {
            if (!release.TryGetProperty("assets", out var assets))
                return null;

            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                if (name?.StartsWith("DMS-Setup-", StringComparison.OrdinalIgnoreCase) == true
                    && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    return asset.GetProperty("browser_download_url").GetString();
            }

            return null;
        }

        private static async Task DownloadAndInstallUpdateAsync(string downloadUrl, string version)
        {
            try
            {
                var temporaryDirectory = Path.Combine(Path.GetTempPath(), "DMS-update");
                Directory.CreateDirectory(temporaryDirectory);
                var installerPath = Path.Combine(temporaryDirectory, $"DMS-Setup-{version}.exe");

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("DMS-Update-Installer/1.0");
                using var installerStream = await client.GetStreamAsync(downloadUrl);
                using var installerFile = File.Create(installerPath);
                await installerStream.CopyToAsync(installerFile);

                Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(
                        "The update has been downloaded. DMS will close and the installer will request permission to upgrade it.",
                        "Installing DMS update",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    Process.Start(new ProcessStartInfo(installerPath)
                    {
                        UseShellExecute = true,
                        Verb = "runas"
                    });
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
