using System.Windows;
using System.Windows.Input;
using DMS.Models;
using DMS.Services;
using DMS.ViewModels;

namespace DMS.Views
{
    public partial class LoginWindow : Window
    {
        private readonly LoginViewModel _viewModel;
        private readonly IUserService _userService;

        public LoginWindow(IUserService userService)
        {
            InitializeComponent();

            _userService = userService;
            _viewModel = new LoginViewModel(userService);
            DataContext = _viewModel;

            _viewModel.NavigateToRegister = OnNavigateToRegister;
            _viewModel.NavigateToLogin = OnNavigateToLogin;
            _viewModel.LoginSucceeded = OnLoginSucceeded;
            _viewModel.Register.CloseAction = OnRegistrationFinished;

            // PasswordBox.Password can't be bound directly (security restriction),
            // so we push it into the ViewModel just before the command runs.
            PasswordBox.PasswordChanged += (_, _) => _viewModel.Password = PasswordBox.Password;
            RegisterPasswordBox.PasswordChanged += (_, _) => _viewModel.Register.Password = RegisterPasswordBox.Password;
            RegisterConfirmPasswordBox.PasswordChanged += (_, _) => _viewModel.Register.ConfirmPassword = RegisterConfirmPasswordBox.Password;
        }

        private void OnNavigateToRegister()
        {
            PasswordBox.Clear();
            UsernameBox.Clear();
            RegisterPasswordBox.Clear();
            RegisterConfirmPasswordBox.Clear();
            Keyboard.ClearFocus();
        }

        private void OnNavigateToLogin()
        {
            RegisterPasswordBox.Clear();
            RegisterConfirmPasswordBox.Clear();
            Keyboard.ClearFocus();
        }

        private void OnRegistrationFinished()
        {
            _viewModel.IsRegistering = false;
            _viewModel.ErrorMessage = string.Empty;
            _viewModel.Register.ErrorMessage = string.Empty;
            PasswordBox.Clear();
            UsernameBox.Clear();
            RegisterPasswordBox.Clear();
            RegisterConfirmPasswordBox.Clear();
        }

        private void OnLoginSucceeded(User user)
        {
            var mainWindow = new MainWindow(user, _userService);
            mainWindow.Show();
            Close();
        }
    }
}
