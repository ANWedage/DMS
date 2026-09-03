using System.Windows;
using System.Windows.Controls;
using DMS.Models;
using DMS.Services;

namespace DMS.Views;

public partial class MyTasksPage : Page
{
    private readonly IUserService _userService;
    private readonly string _userId;
    private List<AssignedTask> _tasks = new();
    private AssignedTask? _selectedTask;

    public MyTasksPage(IUserService userService, string userId)
    {
        InitializeComponent();
        _userService = userService;
        _userId = userId;
        TaskStatusComboBox.SelectedIndex = 1;
        TaskStatusComboBox.SelectionChanged += (_, _) =>
            BlockedReasonTextBox.Visibility = (TaskStatusComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() == TaskStatuses.Blocked
                ? Visibility.Visible : Visibility.Collapsed;
        Loaded += async (_, _) => await LoadTasksAsync();
    }

    private async Task LoadTasksAsync()
    {
        try
        {
            _tasks = await Task.Run(() => _userService.GetMyTasks(_userId));
            TaskListBox.ItemsSource = _tasks;
            if (_tasks.Count > 0) TaskListBox.SelectedIndex = 0;
        }
        catch (Exception ex) { MessageText.Text = $"Unable to load your tasks: {ex.Message}"; }
    }

    private async void TaskListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedTask = TaskListBox.SelectedItem as AssignedTask;
        if (_selectedTask == null)
        {
            ComponentTitleText.Text = "Select a task"; HistoryListView.ItemsSource = null; return;
        }

        ComponentTitleText.Text = _selectedTask.Component.Name;
        ProjectText.Text = $"Project: {_selectedTask.Project.Name}";
        DescriptionText.Text = _selectedTask.Component.Description;
        DueDateText.Text = $"Due: {_selectedTask.Component.DueDate:d} | Status: {_selectedTask.Component.Status}";
        var updates = await Task.Run(() => _userService.GetTaskUpdates(_selectedTask.Component.Id, _userId, false));
        HistoryListView.ItemsSource = updates;
        var today = updates.FirstOrDefault(update => update.UpdateDate.Date == DateTime.Today);
        DailyDescriptionTextBox.Text = today?.Description ?? string.Empty;
        TaskStatusComboBox.SelectedIndex = Array.FindIndex(new[] { TaskStatuses.NotStarted, TaskStatuses.InProgress, TaskStatuses.Blocked, TaskStatuses.Completed }, status => status == (today?.Status ?? TaskStatuses.InProgress));
        BlockedReasonTextBox.Text = today?.BlockedReason ?? string.Empty;
        MessageText.Text = string.Empty;
    }

    private async void SaveUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTask == null) { MessageText.Text = "Select a task first."; return; }
        var status = (TaskStatusComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? TaskStatuses.InProgress;
        var description = DailyDescriptionTextBox.Text.Trim();
        var blockedReason = BlockedReasonTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(description)) { MessageText.Text = "Describe the work completed today."; return; }
        if (status == TaskStatuses.Blocked && string.IsNullOrWhiteSpace(blockedReason)) { MessageText.Text = "Explain what is blocking this task."; return; }
        if (MessageBox.Show("Save today's task update?", "Confirm daily update", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            var update = new DailyTaskUpdate
            {
                ComponentId = _selectedTask.Component.Id, UserId = _userId, UpdateDate = DateTime.Today,
                Description = description, Status = status, BlockedReason = blockedReason
            };
            await Task.Run(() => _userService.SaveDailyTaskUpdate(update));
            MessageText.Text = "Today's update saved successfully.";
            await TaskListBox_SelectionChangedAsync();
        }
        catch (Exception ex) { MessageText.Text = $"Unable to save today's update: {ex.Message}"; }
    }

    private async Task TaskListBox_SelectionChangedAsync()
    {
        if (_selectedTask == null) return;
        var updates = await Task.Run(() => _userService.GetTaskUpdates(_selectedTask.Component.Id, _userId, false));
        HistoryListView.ItemsSource = updates;
    }
}