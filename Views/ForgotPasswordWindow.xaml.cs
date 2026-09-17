using System.Windows;
using System.Windows.Controls;
using DMS.Services;

namespace DMS.Views
{
    public partial class ForgotPasswordWindow : Window
    {
        private readonly IUserService _userService;
        private bool _isUsernameVerified;
        private string _resolvedAccountType = string.Empty;

        public ForgotPasswordWindow(IUserService userService)
        {
            InitializeComponent();
            _userService = userService;
            NextButton.Content = "Check account";
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Text = string.Empty;
            AccountStatusText.Text = string.Empty;

            if (!_isUsernameVerified)
            {
                var username = UsernameBox.Text.Trim();

                if (string.IsNullOrWhiteSpace(username))
                {
                    ErrorText.Text = "Enter a username first.";
                    return;
                }

                var isUser = _userService.UsernameExistsForRole(username, false);
                var isAdmin = _userService.UsernameExistsForRole(username, true);

                if (isUser && isAdmin)
                {
                    ErrorText.Text = "This username matches both a user and an admin account. Please contact support.";
                    return;
                }

                if (!isUser && !isAdmin)
                {
                    ErrorText.Text = "No account was found for that username.";
                    return;
                }

                _resolvedAccountType = isAdmin ? "Admin" : "User";
                _isUsernameVerified = true;
                UsernameBox.IsReadOnly = true;
                AccountStatusText.Text = $"Account found: {_resolvedAccountType} account";
                AccountStatusText.Visibility = Visibility.Visible;
                PasswordPanel.Visibility = Visibility.Visible;
                NextButton.Content = "Reset password";
                ErrorText.Text = string.Empty;
                return;
            }

            var newPassword = NewPasswordBox.Password;
            var confirmPassword = ConfirmPasswordBox.Password;
            var resetUsername = UsernameBox.Text.Trim();
            var resetIsAdmin = string.Equals(_resolvedAccountType, "Admin", StringComparison.OrdinalIgnoreCase);

            if (ShowPasswordCheckBox.IsChecked == true)
            {
                newPassword = NewPasswordTextBox.Text;
                confirmPassword = ConfirmPasswordTextBox.Text;
            }

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
                if (MessageBox.Show($"Reset the password for the {_resolvedAccountType} account named '{resetUsername}'?", "Confirm password reset",
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

        private void ShowPasswordCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            SyncPasswordVisibility(true);
        }

        private void ShowPasswordCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            SyncPasswordVisibility(false);
        }

        private void SyncPasswordVisibility(bool showVisibleText)
        {
            var newPasswordValue = NewPasswordBox.Password;
            var confirmPasswordValue = ConfirmPasswordBox.Password;

            NewPasswordBox.Visibility = showVisibleText ? Visibility.Collapsed : Visibility.Visible;
            NewPasswordTextBox.Visibility = showVisibleText ? Visibility.Visible : Visibility.Collapsed;
            ConfirmPasswordBox.Visibility = showVisibleText ? Visibility.Collapsed : Visibility.Visible;
            ConfirmPasswordTextBox.Visibility = showVisibleText ? Visibility.Visible : Visibility.Collapsed;

            if (showVisibleText)
            {
                NewPasswordTextBox.Text = newPasswordValue;
                ConfirmPasswordTextBox.Text = confirmPasswordValue;
            }
            else
            {
                NewPasswordBox.Password = NewPasswordTextBox.Text;
                ConfirmPasswordBox.Password = ConfirmPasswordTextBox.Text;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
