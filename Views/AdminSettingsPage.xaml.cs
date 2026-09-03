using System.Windows;
using System.Windows.Controls;
using DMS.Helpers;
using DMS.Services;

namespace DMS.Views
{
    public partial class AdminSettingsPage : Page
    {
        private readonly IUserService _userService;
        private readonly Action _passwordChanged;

        public AdminSettingsPage(IUserService userService, Action passwordChanged)
        {
            InitializeComponent();
            _userService = userService;
            _passwordChanged = passwordChanged;
            CurrentUsernameText.Text = AppSession.CurrentUsername ?? string.Empty;
        }

        private void UpdateUsernameButton_Click(object sender, RoutedEventArgs e)
        {
            UsernameErrorText.Text = string.Empty;
            if (string.IsNullOrWhiteSpace(NewUsernameBox.Text) || string.IsNullOrWhiteSpace(UsernameCurrentPasswordBox.Password))
            {
                UsernameErrorText.Text = "Enter the new username and current password.";
                return;
            }

            if (MessageBox.Show("Update your admin username?", "Confirm username change", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try
            {
                var admin = _userService.UpdateAdminUsername(AppSession.CurrentUserId ?? string.Empty,
                    UsernameCurrentPasswordBox.Password, NewUsernameBox.Text);
                CurrentUsernameText.Text = admin.Username;
                NewUsernameBox.Clear();
                UsernameCurrentPasswordBox.Clear();
                MessageBox.Show("Your username was updated.", "Settings updated", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                UsernameErrorText.Text = ex.Message;
            }
        }

        private void UpdatePasswordButton_Click(object sender, RoutedEventArgs e)
        {
            PasswordErrorText.Text = string.Empty;
            if (string.IsNullOrWhiteSpace(PasswordCurrentPasswordBox.Password)
                || string.IsNullOrWhiteSpace(NewPasswordBox.Password)
                || string.IsNullOrWhiteSpace(ConfirmPasswordBox.Password))
            {
                PasswordErrorText.Text = "Complete all password fields.";
                return;
            }
            if (!string.Equals(NewPasswordBox.Password, ConfirmPasswordBox.Password, StringComparison.Ordinal))
            {
                PasswordErrorText.Text = "The new passwords do not match.";
                return;
            }

            if (MessageBox.Show("Update your admin password? You will need to sign in again.", "Confirm password change", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try
            {
                _userService.UpdateAdminPassword(AppSession.CurrentUserId ?? string.Empty,
                    PasswordCurrentPasswordBox.Password, NewPasswordBox.Password);
                MessageBox.Show("Your password was updated. Please sign in again.", "Settings updated", MessageBoxButton.OK, MessageBoxImage.Information);
                _passwordChanged();
            }
            catch (Exception ex)
            {
                PasswordErrorText.Text = ex.Message;
            }
        }
    }
}