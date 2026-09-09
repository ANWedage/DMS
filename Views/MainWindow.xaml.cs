using System.Windows;
using System.Diagnostics;
using System.Windows.Navigation;
using DMS.Helpers;
using DMS.Models;
using DMS.Services;
using DMS.ViewModels;
using Microsoft.AspNetCore.SignalR.Client;
using System.Windows.Threading;

namespace DMS.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly IUserService _userService;
        private readonly string _currentUserId;
        private readonly HubConnection? _chatConnection;
        private IDisposable? _chatMessageSubscription;
        private IDisposable? _notificationSubscription;
        private readonly DispatcherTimer _taskReminderTimer = new() { Interval = TimeSpan.FromMinutes(1) };
        private string? _taskReminderTimeZoneId = "Sri Lanka Standard Time";

        public MainWindow(User currentUser, IUserService userService)
        {
            InitializeComponent();
            Activated += MainWindow_Activated;
            _taskReminderTimer.Tick += async (_, _) => await UpdateTaskReminderBannerAsync();
            _taskReminderTimer.Start();

            _userService = userService;
            if (userService is ApiUserService api)
            {
                _chatConnection = api.CreateChatConnection();
                _chatMessageSubscription = _chatConnection.On<ChatMessage>("ReceiveMessage", message => { _ = UpdateChatCountAsync(); });
                _notificationSubscription = _chatConnection.On("ReceiveNotification", () => { _ = UpdateNotificationCountAsync(); });
                _ = StartChatConnectionAsync();
            }

            if (!userService.CanAccessUser(currentUser.Id))
                throw new InvalidOperationException("You do not have access to this workspace.");

            var safeUser = userService.GetUserById(AppSession.CurrentUserId ?? currentUser.Id);
            _currentUserId = safeUser.Id;
            _viewModel = new MainViewModel(safeUser);
            DataContext = _viewModel;
            _viewModel.LogoutRequested = OnLogoutRequested;
            ShowAttendance();
            _ = UpdateNotificationCountAsync();
            _ = UpdateChatCountAsync();
            _ = UpdateTaskReminderBannerAsync();
        }

        private void ShowAttendance()
        {
            MainContentFrame.Navigate(new AttendancePage(_userService, _currentUserId));
        }

        private void AttendanceButton_Click(object sender, RoutedEventArgs e)
        {
            MainContentFrame.Navigate(new AttendancePage(_userService, _currentUserId));
        }

        private void MyTasksButton_Click(object sender, RoutedEventArgs e)
        {
            MainContentFrame.Navigate(new MyTasksPage(_userService, _currentUserId));
        }

        private async void DailyTaskFormButton_Click(object sender, RoutedEventArgs e)
        {
            await OpenConfiguredFormAsync(settings => settings.DailyTaskFormLink, "daily task");
        }

        private async void LeaveFormButton_Click(object sender, RoutedEventArgs e)
        {
            await OpenConfiguredFormAsync(settings => settings.LeaveFormLink, "leave");
        }

        private async Task OpenConfiguredFormAsync(Func<MeetingSettings, string> linkSelector, string formName)
        {
            try
            {
                var settings = await Task.Run(_userService.GetMeetingSettings);
                var link = linkSelector(settings);
                if (!Uri.TryCreate(link, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                {
                    MessageBox.Show($"The {formName} form link has not been configured by an administrator.", "Form unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open the {formName} form: {ex.Message}", "Form unavailable", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ChatButton_Click(object sender, RoutedEventArgs e)
        {
            _ = UpdateChatCountAsync();
            MainContentFrame.Navigate(new ChatPage(_userService, _currentUserId, "User", _chatConnection, () => _ = UpdateChatCountAsync()));
        }

        private void MainWindow_Activated(object? sender, EventArgs e)
        {
            _ = UpdateNotificationCountAsync();
            _ = UpdateChatCountAsync();
            _ = UpdateTaskReminderBannerAsync();
        }

        private async Task UpdateTaskReminderBannerAsync()
        {
            var now = await GetTaskReminderNowAsync();
            var currentTime = now.TimeOfDay;
            var visibleWindow = IsTaskReminderTestMode()
                || (currentTime >= TimeSpan.FromHours(16).Add(TimeSpan.FromMinutes(50))
                    && currentTime < TimeSpan.FromHours(17).Add(TimeSpan.FromMinutes(10)));
            if (!visibleWindow)
            {
                UserTaskReminderBanner.Visibility = Visibility.Collapsed;
                return;
            }

            UserTaskReminderBanner.Visibility = Visibility.Visible;
        }

        private async Task<DateTime> GetTaskReminderNowAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_taskReminderTimeZoneId))
                    _taskReminderTimeZoneId = (await Task.Run(_userService.GetMeetingSettings)).TimeZoneId;
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
                    TimeZoneInfo.FindSystemTimeZoneById(_taskReminderTimeZoneId));
            }
            catch
            {
                return DateTime.UtcNow.AddHours(5.5);
            }
        }

        private static bool IsTaskReminderTestMode() =>
            string.Equals(Environment.GetEnvironmentVariable("DMS_TASK_REMINDER_TEST"), "true", StringComparison.OrdinalIgnoreCase);

        private void NotificationsButton_Click(object sender, RoutedEventArgs e)
        {
            MainContentFrame.Navigate(new NotificationsPage(_userService, () => _ = UpdateNotificationCountAsync()));
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            MainContentFrame.Navigate(new UserSettingsPage(_userService, OnProfileChanged));
        }

        private void WebsiteHyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private void OnProfileChanged()
        {
            _ = DisposeChatConnectionAsync();
            AppSession.Clear();
            var loginWindow = new LoginWindow(_userService);
            loginWindow.Show();
            Close();
        }

        private async Task UpdateNotificationCountAsync()
        {
            try
            {
                var count = await Task.Run(() => _userService.GetUnreadNotificationCount(_currentUserId, "User"));
                UserNotificationCountText.Text = count > 99 ? "99+" : count.ToString();
                UserNotificationBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
                UserNotificationBadge.Visibility = Visibility.Collapsed;
            }
        }

        private async Task UpdateChatCountAsync()
        {
            try
            {
                var count = await Task.Run(() => _userService.GetUnreadChatCount(_currentUserId, "User"));
                UserChatCountText.Text = count > 99 ? "99+" : count.ToString();
                UserChatBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
                UserChatBadge.Visibility = Visibility.Collapsed;
            }
        }

        private void SignOutButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to sign out?",
                "Sign out",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
                OnLogoutRequested();
        }

        private void OnLogoutRequested()
        {
            _taskReminderTimer.Stop();
            _ = DisposeChatConnectionAsync();
            AppSession.Clear();
            var loginWindow = new LoginWindow(_userService);
            loginWindow.Show();
            Close();
        }

        private async Task StartChatConnectionAsync()
        {
            if (_chatConnection == null) return;
            try { await _chatConnection.StartAsync(); }
            catch { }
        }

        private async Task DisposeChatConnectionAsync()
        {
            _chatMessageSubscription?.Dispose();
            _notificationSubscription?.Dispose();
            if (_chatConnection != null)
                await _chatConnection.DisposeAsync();
        }
    }
}
