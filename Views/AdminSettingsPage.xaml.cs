using System.Windows;
using System.Windows.Controls;
using DMS.Helpers;
using DMS.Models;
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
            CurrentUsernameBox.Text = AppSession.CurrentUsername ?? string.Empty;
        }

        private void UpdateProfileButton_Click(object sender, RoutedEventArgs e)
        {
            ProfileErrorText.Text = string.Empty;
            if (string.IsNullOrWhiteSpace(NewUsernameBox.Text)
                || string.IsNullOrWhiteSpace(PasswordCurrentPasswordBox.Password)
                || string.IsNullOrWhiteSpace(NewPasswordBox.Password)
                || string.IsNullOrWhiteSpace(ConfirmPasswordBox.Password))
            {
                ProfileErrorText.Text = "Complete all profile fields.";
                return;
            }
            if (!string.Equals(NewPasswordBox.Password, ConfirmPasswordBox.Password, StringComparison.Ordinal))
            {
                ProfileErrorText.Text = "The new passwords do not match.";
                return;
            }

            if (MessageBox.Show("Update your username and password? You will need to sign in again.", "Confirm profile update", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try
            {
                _userService.UpdateAdminProfile(AppSession.CurrentUserId ?? string.Empty,
                    new AdminProfileUpdate(CurrentUsernameBox.Text, NewUsernameBox.Text,
                        PasswordCurrentPasswordBox.Password, NewPasswordBox.Password));
                MessageBox.Show("Your profile was updated. Please sign in again.", "Settings updated", MessageBoxButton.OK, MessageBoxImage.Information);
                _passwordChanged();
            }
            catch (Exception ex)
            {
                ProfileErrorText.Text = ex.Message;
            }
        }
    }
}