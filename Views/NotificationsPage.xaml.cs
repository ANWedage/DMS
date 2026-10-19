using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using DMS.Helpers;
using DMS.Models;
using DMS.Services;

namespace DMS.Views
{
    public partial class NotificationsPage : Page
    {
        private readonly IUserService _userService;
        private readonly string _recipientId;
        private readonly string _recipientRole;
        private readonly Action? _unreadChanged;
        private List<NotificationRecipient> _recipientOptions = new();
        private bool _showSentHistory;
        private int _activeLoadCount;
        private int _recipientWheelDelta;

        public NotificationsPage(IUserService userService, Action? unreadChanged = null)
        {
            InitializeComponent();
            _userService = userService;
            _recipientId = AppSession.CurrentUserId ?? string.Empty;
            _recipientRole = AppSession.IsAdmin ? "Admin" : "User";
            _unreadChanged = unreadChanged;

            if (AppSession.IsAdmin)
            {
                ComposeColumn.Width = new GridLength(390);
                ComposePanel.Visibility = Visibility.Visible;
                AdminHistoryButtons.Visibility = Visibility.Visible;
                LoadRecipients();
            }

            Loaded += async (_, _) => await LoadNotificationsAsync();
        }

        private async Task LoadNotificationsAsync()
        {
            BeginLoading();
            try
            {
                var notifications = await Task.Run(() => _showSentHistory
                    ? _userService.GetSentNotifications(_recipientId, _recipientRole)
                    : _userService.GetNotifications(_recipientId, _recipientRole));
                foreach (var notification in notifications)
                    notification.CreatedAt = notification.CreatedAt.ToLocalTime();
                var unread = notifications.LongCount(notification => !notification.IsRead);
                NotificationList.ItemsSource = notifications;
                UnreadCountText.Text = unread == 0 ? "You are all caught up." : $"{unread} unread notification{(unread == 1 ? string.Empty : "s")}";
                _unreadChanged?.Invoke();
            }
            catch (Exception ex)
            {
                UnreadCountText.Text = ex.Message;
            }
            finally
            {
                EndLoading();
            }
        }

        private void BeginLoading()
        {
            _activeLoadCount++;
            LoadingOverlay.Visibility = Visibility.Visible;
            RefreshButton.IsEnabled = false;
            MarkAllReadButton.IsEnabled = false;
            AdminInboxButton.IsEnabled = false;
            AdminSentButton.IsEnabled = false;
        }

        private void EndLoading()
        {
            _activeLoadCount = Math.Max(0, _activeLoadCount - 1);
            if (_activeLoadCount > 0)
                return;

            LoadingOverlay.Visibility = Visibility.Collapsed;
            RefreshButton.IsEnabled = true;
            MarkAllReadButton.IsEnabled = true;
            AdminInboxButton.IsEnabled = true;
            AdminSentButton.IsEnabled = true;
        }

        public async Task RefreshAsync()
        {
            if (AppSession.IsAdmin)
                LoadRecipients();

            await LoadNotificationsAsync();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAsync();
        }

        private void LoadRecipients()
        {
            _recipientOptions = GetSelectedRole() == "Admin"
                ? _userService.GetAllAdmins()
                    .Where(admin => admin.Id != AppSession.CurrentUserId)
                    .Select(admin => new NotificationRecipient(admin.Id, $"{admin.Name} ({admin.Username})", "Admin"))
                    .ToList()
                : _userService.GetAllUsers()
                    .Where(user => user.IsActive)
                    .Select(user => new NotificationRecipient(user.Id, $"{user.Username ?? user.Email} ({user.Email})", "User"))
                    .ToList();
            RecipientListBox.ItemsSource = _recipientOptions;
            RecipientSearchBox.Clear();
            RecipientListBox.SelectedItem = null;
            UpdateRecipientSelectionText();
        }

        private string GetSelectedRole() => (RecipientRoleBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "User";

        private void RecipientRoleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsInitialized)
                LoadRecipients();
        }

        private void SendToAllBox_Click(object sender, RoutedEventArgs e)
        {
            var enabled = SendToAllBox.IsChecked != true;
            RecipientSearchBox.IsEnabled = enabled;
            RecipientListBox.IsEnabled = enabled;
            UpdateRecipientSelectionText();
        }

        private void RecipientSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var searchText = RecipientSearchBox.Text.Trim();
            var view = CollectionViewSource.GetDefaultView(_recipientOptions);
            view.Filter = item => item is NotificationRecipient recipient
                && (string.IsNullOrWhiteSpace(searchText)
                    || recipient.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase));
            view.Refresh();
            if (RecipientListBox.SelectedItem is NotificationRecipient selectedRecipient
                && !view.Contains(selectedRecipient))
                RecipientListBox.SelectedItem = null;
            UpdateRecipientSelectionText();
        }

        private void RecipientListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateRecipientSelectionText();
        }

        private void RecipientListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(RecipientListBox);
            if (scrollViewer is null)
                return;

            _recipientWheelDelta += e.Delta;
            while (_recipientWheelDelta >= Mouse.MouseWheelDeltaForOneLine)
            {
                scrollViewer.LineUp();
                _recipientWheelDelta -= Mouse.MouseWheelDeltaForOneLine;
            }

            while (_recipientWheelDelta <= -Mouse.MouseWheelDeltaForOneLine)
            {
                scrollViewer.LineDown();
                _recipientWheelDelta += Mouse.MouseWheelDeltaForOneLine;
            }

            e.Handled = true;
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T match)
                    return match;

                var descendant = FindVisualChild<T>(child);
                if (descendant is not null)
                    return descendant;
            }

            return null;
        }

        private void UpdateRecipientSelectionText()
        {
            RecipientSelectionText.Text = SendToAllBox.IsChecked == true
                ? "This notification will be sent to everyone in this group."
                : RecipientListBox.SelectedItem is NotificationRecipient recipient
                    ? $"Selected: {recipient.DisplayName}"
                    : "Select one recipient from the list.";
        }

        private void MarkReadButton_Click(object sender, RoutedEventArgs e)
        {
            var notificationId = (sender as Button)?.Tag?.ToString();
            if (string.IsNullOrWhiteSpace(notificationId)) return;

            var result = MessageBox.Show("Mark this notification as read?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                _userService.MarkNotificationRead(notificationId, _recipientId, _recipientRole);
                _ = LoadNotificationsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Notification error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DeleteSentNotificationButton_Click(object sender, RoutedEventArgs e)
        {
            if (!AppSession.IsAdmin || !_showSentHistory)
                return;

            var notificationId = (sender as Button)?.Tag?.ToString();
            if (string.IsNullOrWhiteSpace(notificationId))
                return;

            var result = MessageBox.Show(
                "Delete this sent notification? It will also be removed from the recipient's inbox.",
                "Delete sent notification",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                if (!_userService.DeleteSentNotification(notificationId, _recipientId, _recipientRole))
                {
                    MessageBox.Show("The sent notification could not be found or deleted.", "Delete notification", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                await LoadNotificationsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to delete the sent notification: {ex.Message}", "Delete notification", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MarkAllReadButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Mark all notifications as read?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                _userService.MarkAllNotificationsRead(_recipientId, _recipientRole);
                _ = LoadNotificationsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Notification error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AdminInboxButton_Click(object sender, RoutedEventArgs e)
        {
            _showSentHistory = false;
            _ = LoadNotificationsAsync();
        }

        private void AdminSentButton_Click(object sender, RoutedEventArgs e)
        {
            _showSentHistory = true;
            _ = LoadNotificationsAsync();
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            var sendToAll = SendToAllBox.IsChecked == true;
            var selectedRecipient = RecipientListBox.SelectedItem as NotificationRecipient;
            var title = TitleBox.Text.Trim();
            var message = MessageTextBox.Text.Trim();
            ComposeErrorText.Text = string.Empty;

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message))
            {
                ComposeErrorText.Text = "Enter a title and message.";
                return;
            }
            if (!sendToAll && selectedRecipient is null)
            {
                ComposeErrorText.Text = "Select at least one recipient.";
                return;
            }

            var audience = sendToAll ? $"all {GetSelectedRole().ToLowerInvariant()}s" : selectedRecipient!.DisplayName;
            var result = MessageBox.Show($"Send this notification to {audience}?", "Confirm send", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                var count = _userService.SendNotification(AppSession.CurrentUserId ?? string.Empty,
                    AppSession.CurrentDisplayName ?? AppSession.CurrentUsername ?? "Admin", GetSelectedRole(), sendToAll,
                    selectedRecipient is null ? Array.Empty<string>() : new[] { selectedRecipient.Id }, title, message);
                MessageBox.Show($"Notification sent to {count} recipient(s).", "Notification sent", MessageBoxButton.OK, MessageBoxImage.Information);
                TitleBox.Clear();
                MessageTextBox.Clear();
                if (_showSentHistory)
                    _ = LoadNotificationsAsync();
            }
            catch (Exception ex)
            {
                ComposeErrorText.Text = ex.Message;
            }
        }
    }
}
