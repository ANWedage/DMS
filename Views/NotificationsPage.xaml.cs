using System.Windows;
using System.Windows.Controls;
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

        public NotificationsPage(IUserService userService, Action? unreadChanged = null)
        {
            InitializeComponent();
            _userService = userService;
            _recipientId = AppSession.CurrentUserId ?? string.Empty;
            _recipientRole = AppSession.IsAdmin ? "Admin" : "User";
            _unreadChanged = unreadChanged;

            if (AppSession.IsAdmin)
            {
                ComposeColumn.Width = new GridLength(330);
                ComposePanel.Visibility = Visibility.Visible;
                LoadRecipients();
            }

            Loaded += async (_, _) => await LoadNotificationsAsync();
        }

        private async Task LoadNotificationsAsync()
        {
            try
            {
                var notifications = await Task.Run(() => _userService.GetNotifications(_recipientId, _recipientRole));
                var unread = notifications.LongCount(notification => !notification.IsRead);
                NotificationList.ItemsSource = notifications;
                UnreadCountText.Text = unread == 0 ? "You are all caught up." : $"{unread} unread notification{(unread == 1 ? string.Empty : "s")}";
                _unreadChanged?.Invoke();
            }
            catch (Exception ex)
            {
                UnreadCountText.Text = ex.Message;
            }
        }

        private void LoadRecipients()
        {
            RecipientList.ItemsSource = GetSelectedRole() == "Admin"
                ? _userService.GetAllAdmins()
                    .Where(admin => admin.Id != AppSession.CurrentUserId)
                    .Select(admin => new NotificationRecipient(admin.Id, $"{admin.Name} ({admin.Username})", "Admin"))
                    .ToList()
                : _userService.GetAllUsers()
                    .Where(user => user.IsActive)
                    .Select(user => new NotificationRecipient(user.Id, $"{user.Username ?? user.Email} ({user.Email})", "User"))
                    .ToList();
        }

        private string GetSelectedRole() => (RecipientRoleBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "User";

        private void RecipientRoleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsInitialized)
                LoadRecipients();
        }

        private void SendToAllBox_Click(object sender, RoutedEventArgs e)
        {
            RecipientList.IsEnabled = SendToAllBox.IsChecked != true;
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

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            var sendToAll = SendToAllBox.IsChecked == true;
            var selectedRecipients = RecipientList.SelectedItems.Cast<NotificationRecipient>().ToList();
            var title = TitleBox.Text.Trim();
            var message = MessageTextBox.Text.Trim();
            ComposeErrorText.Text = string.Empty;

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message))
            {
                ComposeErrorText.Text = "Enter a title and message.";
                return;
            }
            if (!sendToAll && selectedRecipients.Count == 0)
            {
                ComposeErrorText.Text = "Select at least one recipient.";
                return;
            }

            var audience = sendToAll ? $"all {GetSelectedRole().ToLowerInvariant()}s" : $"{selectedRecipients.Count} recipient(s)";
            var result = MessageBox.Show($"Send this notification to {audience}?", "Confirm send", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            try
            {
                var count = _userService.SendNotification(AppSession.CurrentUserId ?? string.Empty,
                    AppSession.CurrentDisplayName ?? AppSession.CurrentUsername ?? "Admin", GetSelectedRole(), sendToAll,
                    selectedRecipients.Select(recipient => recipient.Id).ToList(), title, message);
                MessageBox.Show($"Notification sent to {count} recipient(s).", "Notification sent", MessageBoxButton.OK, MessageBoxImage.Information);
                TitleBox.Clear();
                MessageTextBox.Clear();
            }
            catch (Exception ex)
            {
                ComposeErrorText.Text = ex.Message;
            }
        }
    }
}
