using System.Windows;
using DMS.Helpers;
using DMS.Services;

namespace DMS.Views
{
    public partial class AdminWindow : Window
    {
        private readonly IUserService _userService;

        public AdminWindow() : this(new UserService(new Data.MongoDbContext()))
        {
        }

        public AdminWindow(IUserService userService)
        {
            InitializeComponent();
            _userService = userService;
            ShowDevelopers();
            _ = UpdateNotificationCountAsync();
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

        private void NotificationsButton_Click(object sender, RoutedEventArgs e)
        {
            MainContentFrame.Navigate(new NotificationsPage(_userService, () => _ = UpdateNotificationCountAsync()));
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            MainContentFrame.Navigate(new AdminSettingsPage(_userService, OnPasswordChanged));
        }

        private void OnPasswordChanged()
        {
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

        private void SignOutButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to sign out from admin mode?",
                "Sign out",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                AppSession.Clear();
                var loginWindow = new LoginWindow(_userService);
                loginWindow.Show();
                Close();
            }
        }
    }
}
