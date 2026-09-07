using System.Windows;
using System.Diagnostics;
using System.Windows.Navigation;
using DMS.Helpers;
using DMS.Models;
using DMS.Services;
using Microsoft.AspNetCore.SignalR.Client;

namespace DMS.Views
{
    public partial class AdminWindow : Window
    {
        private readonly IUserService _userService;
        private readonly HubConnection? _chatConnection;
        private IDisposable? _chatMessageSubscription;

        public AdminWindow() : this(new UserService(new Data.MongoDbContext()))
        {
        }

        public AdminWindow(IUserService userService)
        {
            InitializeComponent();
            Activated += AdminWindow_Activated;
            _userService = userService;
            if (userService is ApiUserService api)
            {
                _chatConnection = api.CreateChatConnection();
                _chatMessageSubscription = _chatConnection.On<ChatMessage>("ReceiveMessage", message => { _ = UpdateChatCountAsync(); });
                _ = StartChatConnectionAsync();
            }
            ShowDevelopers();
            _ = UpdateNotificationCountAsync();
            _ = UpdateChatCountAsync();
        }

        private void ShowDevelopers()
        {
            MainContentFrame.Navigate(new AllDevelopersPage(_userService));
        }

        private void AllDevelopersButton_Click(object sender, RoutedEventArgs e)
        {
            MainContentFrame.Navigate(new AllDevelopersPage(_userService));
        }

        private void TasksButton_Click(object sender, RoutedEventArgs e)
        {
            MainContentFrame.Navigate(new TasksPage(_userService));
        }

        private void AttendanceTrackingButton_Click(object sender, RoutedEventArgs e)
        {
            MainContentFrame.Navigate(new AttendanceTrackingPage(_userService));
        }

        private void ChatButton_Click(object sender, RoutedEventArgs e)
        {
            _ = UpdateChatCountAsync();
            MainContentFrame.Navigate(new ChatPage(_userService, AppSession.CurrentUserId ?? string.Empty, "Admin", _chatConnection, () => _ = UpdateChatCountAsync()));
        }

        private void AdminWindow_Activated(object? sender, EventArgs e)
        {
            _ = UpdateChatCountAsync();
        }

        private void NotificationsButton_Click(object sender, RoutedEventArgs e)
        {
            MainContentFrame.Navigate(new NotificationsPage(_userService, () => _ = UpdateNotificationCountAsync()));
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            MainContentFrame.Navigate(new AdminSettingsPage(_userService, OnPasswordChanged));
        }

        private void WebsiteHyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }

        private void OnPasswordChanged()
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
                var count = await Task.Run(() => _userService.GetUnreadNotificationCount(AppSession.CurrentUserId ?? string.Empty, "Admin"));
                AdminNotificationCountText.Text = count > 99 ? "99+" : count.ToString();
                AdminNotificationBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
                AdminNotificationBadge.Visibility = Visibility.Collapsed;
            }
        }

        private async Task UpdateChatCountAsync()
        {
            try
            {
                var count = await Task.Run(() => _userService.GetUnreadChatCount(AppSession.CurrentUserId ?? string.Empty, "Admin"));
                AdminChatCountText.Text = count > 99 ? "99+" : count.ToString();
                AdminChatBadge.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
                AdminChatBadge.Visibility = Visibility.Collapsed;
            }
        }

        private void SignOutButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to sign out from admin mode?",
                "Sign out",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _ = DisposeChatConnectionAsync();
                AppSession.Clear();
                var loginWindow = new LoginWindow(_userService);
                loginWindow.Show();
                Close();
            }
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
            if (_chatConnection != null)
                await _chatConnection.DisposeAsync();
        }
    }
}
