using System.Windows;
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

        public MainWindow(User currentUser, IUserService userService)
        {
            InitializeComponent();

            _userService = userService;

            if (!userService.CanAccessUser(currentUser.Id))
                throw new InvalidOperationException("You do not have access to this workspace.");

            var safeUser = userService.GetUserById(AppSession.CurrentUserId ?? currentUser.Id);
            _viewModel = new MainViewModel(safeUser);
            DataContext = _viewModel;
            _viewModel.LogoutRequested = OnLogoutRequested;
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
