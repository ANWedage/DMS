using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using DMS.Helpers;
using DMS.Models;
using DMS.Services;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Win32;

namespace DMS.Views;

public partial class ChatPage : Page
{
    private readonly IUserService _userService;
    private readonly string _currentUserId;
    private readonly string _currentRole;
    private readonly ObservableCollection<ChatConversationSummary> _conversations = new();
    private readonly ObservableCollection<ChatUser> _users = new();
    private readonly ObservableCollection<ChatMessageRow> _messages = new();
    private readonly ICollectionView _usersView;
    private HubConnection? _connection;
    private readonly bool _ownsConnection;
    private readonly Action? _chatCountChanged;
    private IDisposable? _messageSubscription;
    private ChatUser? _selectedUser;
    private bool _showSent;
    private bool _isLoading;
    private string? _pendingAttachmentId;
    private string? _pendingAttachmentFileName;
    private long _pendingAttachmentSizeBytes;
    private byte[]? _pendingAttachmentBytes;

    public ChatPage(IUserService userService, string currentUserId, string currentRole, HubConnection? connection = null, Action? chatCountChanged = null)
    {
        InitializeComponent();
        _userService = userService;
        _currentUserId = currentUserId;
        _currentRole = currentRole;
        _connection = connection;
        _ownsConnection = connection == null;
        _chatCountChanged = chatCountChanged;
        ConversationList.ItemsSource = _conversations;
        _usersView = new ListCollectionView(_users);
        PeopleList.ItemsSource = _usersView;
        MessageList.ItemsSource = _messages;
        Loaded += ChatPage_Loaded;
        Unloaded += ChatPage_Unloaded;
    }

    private async void ChatPage_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadAsync();
        if (_connection == null && _userService is ApiUserService api)
            _connection = api.CreateChatConnection();

        if (_connection != null)
        {
            _messageSubscription = _connection.On<ChatMessage>("ReceiveMessage", message => Dispatcher.InvokeAsync(() => HandleIncomingMessage(message)));
            if (_connection.State == HubConnectionState.Disconnected)
            {
                try { await _connection.StartAsync(); }
                catch { ChatStatusText.Text = "Realtime connection unavailable. Messages will still be saved."; }
            }
        }
    }

    private async void ChatPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _messageSubscription?.Dispose();
        if (_ownsConnection && _connection != null)
            await _connection.DisposeAsync();
    }

    private async Task LoadAsync()
    {
        if (_isLoading)
            return;

        _isLoading = true;
        try
        {
            ChatStatusText.Text = "Loading chat...";
            var conversationsTask = Task.Run(() => _showSent
                ? _userService.GetChatSent(_currentUserId, _currentRole)
                : _userService.GetChatInbox(_currentUserId, _currentRole));
            await Task.WhenAll(conversationsTask, LoadUsersAsync());

            _conversations.Clear();
                foreach (var conversation in conversationsTask.Result)
                {
                    _conversations.Add(conversation with
                    {
                        LatestMessageAt = conversation.LatestMessageAt.ToLocalTime()
                    });
                }
            ChatStatusText.Text = string.Empty;
        }
        catch (Exception ex) { ChatStatusText.Text = $"Unable to load chat: {ex.Message}"; }
        finally
        {
            _isLoading = false;
        }
    }

    public async Task RefreshAsync()
    {
        await LoadAsync();
        if (_selectedUser != null)
            await LoadConversationAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private void CloseChatButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedUser = null;
        _messages.Clear();
        SelectedUserText.Text = "Select a conversation";
        SelectedRoleText.Text = string.Empty;
        ConversationList.SelectedItem = null;
        PeopleList.SelectedItem = null;
        PeopleList.Visibility = Visibility.Collapsed;
        ConversationList.Visibility = Visibility.Visible;
        SetStatus(string.Empty, false);
    }

    private async Task LoadUsersAsync()
    {
        var users = await Task.Run(() => _userService.GetChatUsers(_currentUserId, _currentRole));
        _users.Clear();
        foreach (var user in users) _users.Add(user);
    }

    private async void ConversationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ConversationList.SelectedItem is not ChatConversationSummary summary) return;
        _selectedUser = _users.FirstOrDefault(u => u.Id == summary.OtherUserId && u.Role == summary.OtherRole)
            ?? new ChatUser(summary.OtherUserId, summary.OtherDisplayName, summary.OtherRole);
        await LoadConversationAsync();
    }

    private void SetStatus(string message, bool isError)
    {
        ChatStatusText.Text = message;
        ChatStatusBadge.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
        ChatStatusBadge.Background = isError ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(253, 237, 237)) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(234, 247, 242));
        ChatStatusBadge.BorderBrush = isError ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(243, 194, 194)) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(183, 220, 203));
        ChatStatusText.Foreground = isError ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(155, 44, 44)) : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(13, 85, 88));
    }

    private async void DeleteChatMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.Tag is not ChatConversationSummary summary)
            return;

        var result = MessageBox.Show(
            $"Delete the chat with {summary.OtherDisplayName}? All messages in this conversation will be permanently deleted.",
            "Delete chat",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            var deleted = await Task.Run(() => _userService.DeleteChatConversation(
                _currentUserId, _currentRole, summary.OtherUserId, summary.OtherRole));
            if (!deleted)
            {
                SetStatus("The chat could not be found.", true);
                return;
            }

            _selectedUser = null;
            _messages.Clear();
            SelectedUserText.Text = "Select a conversation";
            SelectedRoleText.Text = string.Empty;
            ConversationList.SelectedItem = null;
            _chatCountChanged?.Invoke();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Unable to delete chat: {ex.Message}", true);
        }
    }

    private async void PeopleList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PeopleList.SelectedItem is not ChatUser user) return;
        _selectedUser = user;
        PeopleList.Visibility = Visibility.Collapsed;
        ConversationList.Visibility = Visibility.Visible;
        await LoadConversationAsync();
    }

    private async Task LoadConversationAsync()
    {
        if (_selectedUser == null) return;
        SelectedUserText.Text = _selectedUser.DisplayName;
        SelectedRoleText.Text = _selectedUser.Role;
        try
        {
            var history = await Task.Run(() => _userService.GetChatMessages(_currentUserId, _currentRole, _selectedUser.Id, _selectedUser.Role));
            _messages.Clear();
            foreach (var message in history) AddMessage(message);
            await Task.Run(() => _userService.MarkChatMessagesRead(_currentUserId, _currentRole, _selectedUser.Id, _selectedUser.Role));
            _chatCountChanged?.Invoke();
            await LoadAsync();
        }
        catch (Exception ex) { SetStatus($"Unable to load conversation: {ex.Message}", true); }
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e) => await SendMessageAsync();

    private async void MessageTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
        {
            e.Handled = true;
            await SendMessageAsync();
        }
    }

    private void MessageTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        MessagePlaceholderText.Visibility = string.IsNullOrEmpty(MessageTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async Task SendMessageAsync()
    {
        if (_selectedUser == null) return;
        var text = MessageTextBox.Text.Trim();
        var hasAttachment = !string.IsNullOrWhiteSpace(_pendingAttachmentFileName) && _pendingAttachmentBytes != null;
        if (string.IsNullOrWhiteSpace(text) && !hasAttachment) return;

        string? attachmentIdToSend = _pendingAttachmentId;
        try
        {
            if (hasAttachment && string.IsNullOrWhiteSpace(attachmentIdToSend))
            {
                var createdAttachment = await Task.Run(() => _userService.UploadChatAttachment(
                    _currentUserId,
                    _currentRole,
                    _selectedUser.Id,
                    _selectedUser.Role,
                    _pendingAttachmentFileName!,
                    _pendingAttachmentBytes!));

                attachmentIdToSend = createdAttachment.Id;
                _pendingAttachmentId = createdAttachment.Id;
            }

            if (_connection?.State == HubConnectionState.Connected)
                await _connection.InvokeAsync<ChatMessage>("SendMessage", _selectedUser.Id, _selectedUser.Role, text, attachmentIdToSend);
            else
                await Task.Run(() => _userService.SaveChatMessage(_currentUserId, _currentRole, _selectedUser.Id, _selectedUser.Role, text, attachmentIdToSend));

            MessageTextBox.Clear();
            ClearPendingAttachment();
            if (_connection?.State != HubConnectionState.Connected)
                await LoadConversationAsync();
            SetStatus(hasAttachment ? "PDF sent successfully." : "Message sent.", false);
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(attachmentIdToSend))
            {
                try { await Task.Run(() => _userService.DeleteChatAttachment(_currentUserId, _currentRole, attachmentIdToSend)); }
                catch { }
            }

            SetStatus($"Unable to send message: {ex.Message}", true);
        }
    }

    private void UpdateAttachmentPreviewUi()
    {
        var hasPendingAttachment = _pendingAttachmentBytes != null && !string.IsNullOrWhiteSpace(_pendingAttachmentFileName);
        if (!hasPendingAttachment)
        {
            AttachmentPreviewBorder.Visibility = Visibility.Collapsed;
            AttachmentPreviewNameText.Text = string.Empty;
            AttachmentPreviewMetaText.Text = string.Empty;
            return;
        }

        AttachmentPreviewBorder.Visibility = Visibility.Visible;
        AttachmentPreviewNameText.Text = _pendingAttachmentFileName;
        AttachmentPreviewMetaText.Text = FormatFileSize(_pendingAttachmentSizeBytes);
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    private void ClearPendingAttachment()
    {
        _pendingAttachmentId = null;
        _pendingAttachmentFileName = null;
        _pendingAttachmentSizeBytes = 0;
        _pendingAttachmentBytes = null;
        UpdateAttachmentPreviewUi();
    }

    private async void ClearPendingAttachment_Click(object sender, RoutedEventArgs e)
    {
        ClearPendingAttachment();
        SetStatus("PDF selection cleared.", false);
    }

    private async void UploadPdfButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser == null)
        {
            SetStatus("Select a chat partner before uploading a PDF.", true);
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "PDF files (*.pdf)|*.pdf",
            Title = "Select a PDF to share"
        };

        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FileName))
            return;

        try
        {
            var fileInfo = new FileInfo(dialog.FileName);
            if (fileInfo.Length == 0 || fileInfo.Length > 10 * 1024 * 1024)
            {
                SetStatus("The PDF must be larger than zero bytes and no larger than 10 MB.", true);
                return;
            }

            var bytes = await File.ReadAllBytesAsync(dialog.FileName);
            _pendingAttachmentId = null;
            _pendingAttachmentFileName = Path.GetFileName(dialog.FileName);
            _pendingAttachmentSizeBytes = fileInfo.Length;
            _pendingAttachmentBytes = bytes;
            UpdateAttachmentPreviewUi();
            SetStatus($"PDF ready to send: {Path.GetFileName(dialog.FileName)}", false);
        }
        catch (Exception ex)
        {
            SetStatus($"Unable to upload PDF: {ex.Message}", true);
        }
    }

    private async void DownloadAttachmentButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string attachmentId || string.IsNullOrWhiteSpace(attachmentId))
            return;

        try
        {
            var attachment = await Task.Run(() => _userService.GetChatAttachment(_currentUserId, _currentRole, attachmentId));
            if (attachment == null)
            {
                SetStatus("The PDF attachment could not be found.", true);
                return;
            }

            var data = await Task.Run(() => _userService.DownloadChatAttachment(_currentUserId, _currentRole, attachmentId));
            var saveDialog = new SaveFileDialog
            {
                FileName = attachment.OriginalFileName,
                Filter = "PDF files (*.pdf)|*.pdf",
                Title = "Save PDF attachment"
            };

            if (saveDialog.ShowDialog() != true)
                return;

            var payload = Convert.FromBase64String(data.ContentBase64);
            await File.WriteAllBytesAsync(saveDialog.FileName, payload);
            SetStatus($"Downloaded: {attachment.OriginalFileName}", false);
        }
        catch (Exception ex)
        {
            SetStatus($"Unable to download PDF: {ex.Message}", true);
        }
    }

    private async void DeleteAttachmentButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string attachmentId || string.IsNullOrWhiteSpace(attachmentId))
            return;

        var result = MessageBox.Show("Delete this PDF from the chat?", "Delete PDF", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            var deleted = await Task.Run(() => _userService.DeleteChatAttachment(_currentUserId, _currentRole, attachmentId));
            if (deleted)
            {
                if (string.Equals(_pendingAttachmentId, attachmentId, StringComparison.Ordinal))
                    ClearPendingAttachment();

                var row = _messages.FirstOrDefault(m => m.AttachmentId == attachmentId);
                if (row != null)
                {
                    var updated = row with
                    {
                        AttachmentId = null,
                        AttachmentName = null,
                        Text = "Attachment deleted"
                    };
                    var index = _messages.IndexOf(row);
                    _messages[index] = updated;
                }

                SetStatus("PDF deleted from the chat.", false);
                await RefreshAsync();
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Unable to delete PDF: {ex.Message}", true);
        }
    }

    private void HandleIncomingMessage(ChatMessage message)
    {
        var belongsToSelected = _selectedUser != null
            && ((message.SenderId == _currentUserId && message.RecipientId == _selectedUser.Id)
                || (message.RecipientId == _currentUserId && message.SenderId == _selectedUser.Id));
        if (belongsToSelected)
        {
            AddMessage(message);
            if (message.RecipientId == _currentUserId)
                _ = Task.Run(() => _userService.MarkChatMessagesRead(_currentUserId, _currentRole, message.SenderId, message.SenderRole));
                _chatCountChanged?.Invoke();
        }
        _ = LoadAsync();
    }

    private void AddMessage(ChatMessage message)
    {
        if (_messages.Any(row => row.Id == message.Id)) return;

        string? attachmentId = message.AttachmentId;
        string? attachmentName = null;
        var displayedText = message.MessageText;
        var validAttachment = !string.IsNullOrWhiteSpace(attachmentId);

        if (validAttachment)
        {
            var attachment = _userService.GetChatAttachment(_currentUserId, _currentRole, attachmentId!);
            validAttachment = attachment != null;
            attachmentName = attachment?.OriginalFileName;
            if (!validAttachment)
            {
                attachmentId = null;
                displayedText = "Attachment deleted";
            }
            else if (string.IsNullOrWhiteSpace(displayedText))
            {
                displayedText = "Send attachment";
            }
        }

        _messages.Add(new ChatMessageRow(
            message.Id,
            displayedText,
            message.CreatedAt.ToLocalTime(),
            message.SenderId == _currentUserId,
            attachmentId,
            attachmentName));
        MessageList.ScrollIntoView(_messages.Last());
    }

    private void InboxButton_Click(object sender, RoutedEventArgs e)
    {
        _showSent = false;
        ConversationTitle.Text = "Inbox";
        PeopleList.Visibility = Visibility.Collapsed;
        ConversationList.Visibility = Visibility.Visible;
        _ = LoadAsync();
    }

    private void SentButton_Click(object sender, RoutedEventArgs e)
    {
        _showSent = true;
        ConversationTitle.Text = "Sent";
        PeopleList.Visibility = Visibility.Collapsed;
        ConversationList.Visibility = Visibility.Visible;
        _ = LoadAsync();
    }

    private void NewChatButton_Click(object sender, RoutedEventArgs e)
    {
        ConversationTitle.Text = "New chat";
        ConversationList.Visibility = Visibility.Collapsed;
        PeopleList.Visibility = Visibility.Visible;
        SearchTextBox.Clear();
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchTextBox.Text.Trim();
        if (PeopleList.Visibility == Visibility.Visible)
        {
            _usersView.Filter = item => item is ChatUser user
                && (string.IsNullOrWhiteSpace(query)
                    || user.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase));
            _usersView.Refresh();
        }
    }

    private sealed record ChatMessageRow(string Id, string Text, DateTime CreatedAt, bool IsMine, string? AttachmentId = null, string? AttachmentName = null)
    {
        public string AttachmentVisible => string.IsNullOrWhiteSpace(AttachmentId) ? "Collapsed" : "Visible";
        public string DeleteVisible => IsMine && !string.IsNullOrWhiteSpace(AttachmentId) ? "Visible" : "Collapsed";
        public string TextVisible => string.IsNullOrWhiteSpace(Text) ? "Collapsed" : "Visible";
    }
}