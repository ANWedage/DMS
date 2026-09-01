using System.Windows;
using DMS.Services;
using DMS.ViewModels;

namespace DMS.Views
{
    public partial class RegisterWindow : Window
    {
        private readonly RegisterViewModel _viewModel;

        public RegisterWindow(IUserService userService)
        {
            InitializeComponent();

            _viewModel = new RegisterViewModel(userService);
            DataContext = _viewModel;
            _viewModel.CloseAction = Close;

            PasswordBox.PasswordChanged += (_, _) => _viewModel.Password = PasswordBox.Password;
            ConfirmPasswordBox.PasswordChanged += (_, _) => _viewModel.ConfirmPassword = ConfirmPasswordBox.Password;
        }
    }
}
