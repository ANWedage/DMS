using System.Windows;
using System.Windows.Controls;
using DMS.Services;

namespace DMS.Views
{
    public partial class ForgotPasswordWindow : Window
    {
        private readonly IUserService _userService;
        private bool _isUsernameVerified;

        public ForgotPasswordWindow(IUserService userService)
        {
            InitializeComponent();
            _userService = userService;
            NextButton.Content = "Next";
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Text = string.Empty;
            if (!_isUsernameVerified)
            {
                var username = UsernameBox.Text.Trim();
                var isAdmin = ((ComboBoxItem)RoleComboBox.SelectedItem)?.Content?.ToString() == "Admin";

                if (string.IsNullOrWhiteSpace(username))
                {
                    ErrorText.Text = "Enter a username first.";
                    return;
                }

                if (!_userService.UsernameExistsForRole(username, isAdmin))
                {
                    ErrorText.Text = "No account was found for that username.";
                    return;
                }

                _isUsernameVerified = true;
                UsernameBox.IsReadOnly = true;
                RoleComboBox.IsEnabled = false;
                PasswordPanel.Visibility = Visibility.Visible;
                NextButton.Content = "Reset password";
                ErrorText.Text = string.Empty;
                return;
            }

            var newPassword = NewPasswordBox.Password;
            var confirmPassword = ConfirmPasswordBox.Password;
            var resetUsername = UsernameBox.Text.Trim();
            var resetIsAdmin = ((ComboBoxItem)RoleComboBox.SelectedItem)?.Content?.ToString() == "Admin";

            if (string.IsNullOrWhiteSpace(newPassword) || string.IsNullOrWhiteSpace(confirmPassword))
            {
                ErrorText.Text = "Enter and confirm your new password.";
                return;
            }

            if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
            {
                ErrorText.Text = "The new passwords do not match.";
                return;
            }

            try
            {
                if (MessageBox.Show("Reset this password for the selected account?", "Confirm password reset",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                {
                    return;
                }

                if (!_userService.ResetPassword(resetUsername, newPassword, resetIsAdmin))
                {
                    ErrorText.Text = "No account was found for that username.";
                    return;
                }

                MessageBox.Show("Password updated successfully. Please sign in again.",
                    "Password reset successful", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            catch (Exception ex)
            {
                ErrorText.Text = ex.Message;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
