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
            ShowDashboard();
        }

        private void ShowDashboard()
        {
            MainContentFrame.Navigate(new DashboardPage());
        }

        private void DashboardButton_Click(object sender, RoutedEventArgs e)
        {
            MainContentFrame.Navigate(new DashboardPage());
        }

        private void AllDevelopersButton_Click(object sender, RoutedEventArgs e)
        {
            MainContentFrame.Navigate(new AllDevelopersPage(_userService));
        }

        private void TasksButton_Click(object sender, RoutedEventArgs e)
        {
            MainContentFrame.Navigate(new TasksPage());
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
