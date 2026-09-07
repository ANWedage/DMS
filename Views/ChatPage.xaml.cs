using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using DMS.Helpers;
using DMS.Models;
using DMS.Services;
using Microsoft.AspNetCore.SignalR.Client;

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
        try
        {
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
        catch (Exception ex) { ChatStatusText.Text = $"Unable to load conversation: {ex.Message}"; }
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
        if (_selectedUser == null || string.IsNullOrWhiteSpace(MessageTextBox.Text)) return;
        var text = MessageTextBox.Text.Trim();
        try
        {
            if (_connection?.State == HubConnectionState.Connected)
                await _connection.InvokeAsync<ChatMessage>("SendMessage", _selectedUser.Id, _selectedUser.Role, text);
            else
                await Task.Run(() => _userService.SaveChatMessage(_currentUserId, _currentRole, _selectedUser.Id, _selectedUser.Role, text));
            MessageTextBox.Clear();
            if (_connection?.State != HubConnectionState.Connected)
                await LoadConversationAsync();
        }
        catch (Exception ex) { ChatStatusText.Text = $"Unable to send message: {ex.Message}"; }
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
        _messages.Add(new ChatMessageRow(message.Id, message.MessageText, message.CreatedAt.ToLocalTime(), message.SenderId == _currentUserId));
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

    private sealed record ChatMessageRow(string Id, string Text, DateTime CreatedAt, bool IsMine);
}