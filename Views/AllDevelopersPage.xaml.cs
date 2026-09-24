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
        private bool _isLoading;
        private bool _reloadRequested;

        public AllDevelopersPage(IUserService userService)
        {
            InitializeComponent();
            _userService = userService;
            DailyTaskDatePicker.SelectedDate = DateTime.Today;
            Loaded += AllDevelopersPage_Loaded;
        }

        private async void AllDevelopersPage_Loaded(object sender, RoutedEventArgs e)
        {
            await ReloadDevelopersAsync();
        }

        private async Task ReloadDevelopersAsync()
        {
            if (_isLoading)
            {
                _reloadRequested = true;
                return;
            }

            _isLoading = true;
            SetLoadingState(true);
            try
            {
                var selectedDate = DailyTaskDatePicker.SelectedDate?.Date ?? DateTime.Today;
                var result = await Task.Run(() =>
                {
                    var users = _userService.GetAllUsers();
                    var statuses = _userService.GetDeveloperDailyTaskStatus(selectedDate);
                    return (users, statuses);
                });

                var dailyTaskStatuses = result.statuses.ToDictionary(status => status.UserId, StringComparer.Ordinal);

                foreach (var user in result.users)
                {
                    if (dailyTaskStatuses.TryGetValue(user.Id, out var dailyTaskStatus))
                    {
                        user.HasSubmittedDailyTask = dailyTaskStatus.HasSubmittedUpdate;
                        user.DailyTaskDisplayStatus = dailyTaskStatus.DisplayStatus;
                    }
                    else
                    {
                        user.HasSubmittedDailyTask = false;
                        user.DailyTaskDisplayStatus = "Not submitted";
                    }
                }

                _allUsers = result.users
                    .OrderBy(u => string.IsNullOrWhiteSpace(u.Username) ? u.Email : u.Username)
                    .ToList();

                UpdateDeveloperCount();
                UpdateDeactivatedByColumnVisibility();
                ApplyFilter();
            }
            catch (Exception ex)
            {
                DeveloperListView.ItemsSource = new List<User>();
                MessageBox.Show($"Unable to load developers: {ex.Message}", "Developer list", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isLoading = false;
                SetLoadingState(false);
                if (_reloadRequested)
                {
                    _reloadRequested = false;
                    _ = ReloadDevelopersAsync();
                }
            }
        }

        private void SetLoadingState(bool isLoading)
        {
            LoadingOverlay.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            RefreshButton.IsEnabled = !isLoading;
            DailyTaskDatePicker.IsEnabled = !isLoading;
            SearchTextBox.IsEnabled = !isLoading;
            ClearSearchButton.IsEnabled = !isLoading;
        }

        public Task RefreshAsync() => ReloadDevelopersAsync();

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Clear();
            SearchTextBox.Focus();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAsync();
        }

        private async void DailyTaskDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded)
                await ReloadDevelopersAsync();
        }

        private async void StatusComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
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
                await ReloadDevelopersAsync();
                MessageBox.Show($"Developer account status changed to {user.Status}.", "Developer status", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to update the developer status: {ex.Message}", "Developer status", MessageBoxButton.OK, MessageBoxImage.Error);
                comboBox.SelectedValue = user.IsActive ? "Active" : "Inactive";
            }
        }

        private async void PositionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox comboBox || comboBox.Tag is not string userId)
                return;

            var selectedPosition = comboBox.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(selectedPosition))
                return;

            var user = _allUsers.FirstOrDefault(u => u.Id == userId);
            if (user is null)
                return;

            var normalizedPosition = User.NormalizePosition(selectedPosition);
            if (string.Equals(user.Position, normalizedPosition, StringComparison.Ordinal))
                return;

            var result = MessageBox.Show(
                $"Are you sure you want to change {user.Username ?? user.Email} to {normalizedPosition}?",
                "Confirm developer position change",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                comboBox.SelectedItem = user.Position;
                return;
            }

            try
            {
                var updated = _userService.SetUserPosition(userId, normalizedPosition);
                if (!updated)
                {
                    MessageBox.Show("Unable to save the developer position.", "Developer position", MessageBoxButton.OK, MessageBoxImage.Warning);
                    comboBox.SelectedValue = user.Position;
                    return;
                }

                user.Position = normalizedPosition;
                await ReloadDevelopersAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to save the developer position: {ex.Message}", "Developer position", MessageBoxButton.OK, MessageBoxImage.Error);
                comboBox.SelectedValue = user.Position;
            }
        }

        private void LeavingDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not DatePicker datePicker || datePicker.Tag is not string userId)
                return;

            var user = _allUsers.FirstOrDefault(u => u.Id == userId);
            if (user is null || user.LeavingDate?.Date == datePicker.SelectedDate?.Date)
                return;

            var displayName = user.Username ?? user.Email;
            var newDate = datePicker.SelectedDate?.Date;
            var dateDescription = newDate?.ToString("d") ?? "no leaving date";
            var result = MessageBox.Show(
                $"Are you sure you want to set {displayName}'s leaving date to {dateDescription}?",
                "Confirm leaving date change",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                datePicker.SelectedDate = user.LeavingDate;
                return;
            }

            try
            {
                var updated = _userService.SetUserLeavingDate(userId, newDate);
                if (!updated)
                {
                    MessageBox.Show("Unable to save the developer leaving date.", "Leaving date", MessageBoxButton.OK, MessageBoxImage.Warning);
                    datePicker.SelectedDate = user.LeavingDate;
                    return;
                }

                user.LeavingDate = newDate;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to save the developer leaving date: {ex.Message}", "Leaving date", MessageBoxButton.OK, MessageBoxImage.Error);
                datePicker.SelectedDate = user.LeavingDate;
            }
        }

        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
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

                await ReloadDevelopersAsync();
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
                    || (u.ContactNumber ?? string.Empty).Contains(searchText, StringComparison.OrdinalIgnoreCase)
                    || (u.Position ?? string.Empty).Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .ToList();

            DeveloperListView.ItemsSource = filteredUsers;
        }

        private void UpdateDeveloperCount()
        {
            var totalUsers = _allUsers.Count;
            var inactiveUsers = _allUsers.Count(user => !user.IsActive);

            DeveloperCountText.Text = totalUsers.ToString();
            InactiveDeveloperCountText.Text = inactiveUsers.ToString();

            DeveloperCountText.ToolTip = $"{totalUsers} total developers";
            InactiveDeveloperCountText.ToolTip = $"{inactiveUsers} inactive developers";
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
