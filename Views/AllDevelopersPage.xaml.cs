using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DMS.Helpers;
using DMS.Models;
using DMS.Services;

namespace DMS.Views
{
    public partial class AllDevelopersPage : Page
    {
        private readonly IUserService _userService;
        private List<User> _allUsers = new();

        public AllDevelopersPage(IUserService userService)
        {
            InitializeComponent();
            _userService = userService;
            Loaded += AllDevelopersPage_Loaded;
        }

        private async void AllDevelopersPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var users = await Task.Run(() => _userService.GetAllUsers());
                _allUsers = users
                    .OrderBy(u => string.IsNullOrWhiteSpace(u.Username) ? u.Email : u.Username)
                    .ToList();

                UpdateDeactivatedByColumnVisibility();
                ApplyFilter();
            }
            catch (Exception ex)
            {
                DeveloperListView.ItemsSource = new List<User>();
                MessageBox.Show($"Unable to load developers: {ex.Message}", "Developer list", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Clear();
            SearchTextBox.Focus();
        }

        private void StatusComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox comboBox || comboBox.Tag is not string userId)
                return;

            var selectedStatus = comboBox.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(selectedStatus))
                return;

            var user = _allUsers.FirstOrDefault(u => u.Id == userId);
            if (user is null)
                return;

            var newIsActive = string.Equals(selectedStatus, "Active", StringComparison.OrdinalIgnoreCase);
            if (newIsActive == user.IsActive)
                return;

            var result = MessageBox.Show(
                $"Are you sure you want to change {user.Username ?? user.Email} to {selectedStatus}?",
                "Confirm developer status change",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                comboBox.SelectedValue = user.IsActive ? "Active" : "Inactive";
                return;
            }

            try
            {
                var updated = _userService.SetUserStatus(userId, newIsActive, AppSession.CurrentDisplayName);
                if (!updated)
                {
                    MessageBox.Show("Unable to update the developer status.", "Developer status", MessageBoxButton.OK, MessageBoxImage.Warning);
                    comboBox.SelectedValue = user.IsActive ? "Active" : "Inactive";
                    return;
                }

                user.IsActive = newIsActive;
                user.DeactivatedByAdminName = newIsActive ? null : AppSession.CurrentDisplayName;
                UpdateDeactivatedByColumnVisibility();
                ApplyFilter();
                DeveloperListView.Items.Refresh();
                MessageBox.Show($"Developer account status changed to {user.Status}.", "Developer status", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to update the developer status: {ex.Message}", "Developer status", MessageBoxButton.OK, MessageBoxImage.Error);
                comboBox.SelectedValue = user.IsActive ? "Active" : "Inactive";
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string userId)
                return;

            var user = _allUsers.FirstOrDefault(u => u.Id == userId);
            if (user is null)
                return;

            var displayName = user.Username ?? user.Email;
            var result = MessageBox.Show(
                $"Are you sure you want to permanently delete the developer account for {displayName}? This will also delete attendance records.",
                "Confirm account deletion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                if (!_userService.DeleteUserAccount(userId))
                {
                    MessageBox.Show("The developer account could not be found.", "Delete developer", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _allUsers.Remove(user);
                UpdateDeactivatedByColumnVisibility();
                ApplyFilter();
                MessageBox.Show($"Developer account {displayName} was deleted.", "Delete developer", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to delete the developer account: {ex.Message}", "Delete developer", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyFilter()
        {
            var searchText = SearchTextBox?.Text ?? string.Empty;
            var filteredUsers = string.IsNullOrWhiteSpace(searchText)
                ? _allUsers
                : _allUsers.Where(u =>
                    (u.Username ?? string.Empty).Contains(searchText, StringComparison.OrdinalIgnoreCase)
                    || (u.Email ?? string.Empty).Contains(searchText, StringComparison.OrdinalIgnoreCase)
                    || (u.ContactNumber ?? string.Empty).Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .ToList();

            DeveloperListView.ItemsSource = filteredUsers;
        }

        private void UpdateDeactivatedByColumnVisibility()
        {
            var shouldShowColumn = _allUsers.Any(user => !user.IsActive);
            var columnIsVisible = DeveloperGridView.Columns.Contains(DeactivatedByColumn);

            if (shouldShowColumn)
            {
                if (columnIsVisible)
                    DeveloperGridView.Columns.Remove(DeactivatedByColumn);

                DeveloperGridView.Columns.Add(DeactivatedByColumn);
            }
            else if (columnIsVisible)
                DeveloperGridView.Columns.Remove(DeactivatedByColumn);
        }
    }
}
