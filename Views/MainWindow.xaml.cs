using System.Windows;
using System.Diagnostics;
using System.Windows.Navigation;
using DMS.Helpers;
using DMS.Models;
using DMS.Services;
using DMS.ViewModels;

namespace DMS.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly IUserService _userService;
        private readonly string _currentUserId;

        public MainWindow(User currentUser, IUserService userService)
        {
            InitializeComponent();

            _userService = userService;

            if (!userService.CanAccessUser(currentUser.Id))
                throw new InvalidOperationException("You do not have access to this workspace.");

            var safeUser = userService.GetUserById(AppSession.CurrentUserId ?? currentUser.Id);
            _currentUserId = safeUser.Id;
            _viewModel = new MainViewModel(safeUser);
            DataContext = _viewModel;
            _viewModel.LogoutRequested = OnLogoutRequested;
            ShowAttendance();
            _ = UpdateNotificationCountAsync();
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
            AppSession.Clear();
            var loginWindow = new LoginWindow(_userService);
            loginWindow.Show();
            Close();
        }
    }
}
