using System.Windows;
using System.Windows.Controls;
using DMS.Helpers;
using DMS.Models;
using DMS.Services;

namespace DMS.Views
{
    public partial class UserSettingsPage : Page
    {
        private readonly IUserService _userService;
        private readonly Action _profileChanged;

        public UserSettingsPage(IUserService userService, Action profileChanged)
        {
            InitializeComponent();
            _userService = userService;
            _profileChanged = profileChanged;
            CurrentUsernameBox.Text = AppSession.CurrentUsername ?? string.Empty;
        }

        private void UpdateProfileButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Text = string.Empty;
            if (string.IsNullOrWhiteSpace(NewUsernameBox.Text)
                || string.IsNullOrWhiteSpace(CurrentPasswordBox.Password)
                || string.IsNullOrWhiteSpace(NewPasswordBox.Password)
                || string.IsNullOrWhiteSpace(ConfirmPasswordBox.Password))
            {
                ErrorText.Text = "Complete all profile fields.";
                return;
            }
            if (!string.Equals(NewPasswordBox.Password, ConfirmPasswordBox.Password, StringComparison.Ordinal))
            {
                ErrorText.Text = "The new passwords do not match.";
                return;
            }

            if (MessageBox.Show("Update your username and password? You will need to sign in again.",
                "Confirm profile update", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            try
            {
                _userService.UpdateUserProfile(AppSession.CurrentUserId ?? string.Empty,
                    new UserProfileUpdate(CurrentUsernameBox.Text, NewUsernameBox.Text,
                        CurrentPasswordBox.Password, NewPasswordBox.Password));
                MessageBox.Show("Your profile was updated. Please sign in again.",
                    "Settings updated", MessageBoxButton.OK, MessageBoxImage.Information);
                _profileChanged();
            }
            catch (Exception ex)
            {
                ErrorText.Text = ex.Message;
            }
        }
    }
}
