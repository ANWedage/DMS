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
    private string _taskSearchText = string.Empty;
    private string _taskFilter = "All tasks";

    public MyTasksPage(IUserService userService, string userId)
    {
        InitializeComponent();
        _userService = userService;
        _userId = userId;
        UpdateTypeComboBox.SelectedIndex = 0;
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
            ClearUpdateInputs();
            _tasks = await Task.Run(() => _userService.GetMyTasks(_userId));
            ApplyTaskView();
            UpdateTaskSummary();
            if (_tasks.Count > 0)
            {
                TaskListBox.SelectedIndex = 0;
            }
            else
            {
                _selectedTask = null;
                ComponentTitleText.Text = "No active tasks";
                ProjectText.Text = string.Empty;
                DescriptionText.Text = string.Empty;
                DueDateText.Text = string.Empty;
                DailyDescriptionTextBox.Text = string.Empty;
                await LoadDailyHistoryAsync();
            }

        }
        catch (Exception ex) { MessageText.Text = $"Unable to load your tasks: {ex.Message}"; }
    }

    private async void TaskListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedTask = TaskListBox.SelectedItem as AssignedTask;
        if (_selectedTask == null)
        {
            ComponentTitleText.Text = "No active tasks";
            ProjectText.Text = string.Empty;
            DescriptionText.Text = string.Empty;
            DueDateText.Text = string.Empty;
            DailyDescriptionTextBox.Text = string.Empty;
            return;
        }

        ComponentTitleText.Text = _selectedTask.Component.Name;
        ProjectText.Text = $"Project: {_selectedTask.Project.Name}";
        DescriptionText.Text = _selectedTask.Component.Description;
        DueDateText.Text = $"Due: {_selectedTask.Component.DueDate:d} | Status: {_selectedTask.Component.Status}";
        var updates = await Task.Run(() => _userService.GetTaskUpdates(_selectedTask.Component.Id, _userId, false));
        var today = updates.FirstOrDefault(update => update.UpdateDate.Date == DateTime.Today);
        DailyDescriptionTextBox.Text = today?.Description ?? string.Empty;
        TaskStatusComboBox.SelectedIndex = Array.FindIndex(new[] { TaskStatuses.NotStarted, TaskStatuses.InProgress, TaskStatuses.Blocked, TaskStatuses.Completed }, status => status == (today?.Status ?? TaskStatuses.InProgress));
        BlockedReasonTextBox.Text = today?.BlockedReason ?? string.Empty;
        MessageText.Text = string.Empty;
        await LoadDailyHistoryAsync();
    }

    private void TaskSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        _taskSearchText = textBox.Text.Trim();
        if (TaskListBox == null) return;
        ApplyTaskView();
    }

    private void TaskFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item && item.Content is string filter)
        {
            _taskFilter = filter;
            if (TaskListBox == null) return;
            ApplyTaskView();
        }
    }

    private void ApplyTaskView()
    {
        var selectedComponentId = _selectedTask?.Component.Id;
        var search = _taskSearchText;

        var visibleTasks = _tasks.Where(task =>
        {
            var matchesSearch = string.IsNullOrWhiteSpace(search)
                || task.Component.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || task.Project.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || task.Component.Description.Contains(search, StringComparison.OrdinalIgnoreCase);

            var latestStatus = task.LatestUpdate?.Status ?? task.Component.Status;
            var matchesFilter = _taskFilter switch
            {
                "In progress" => latestStatus == TaskStatuses.InProgress,
                "Due soon" => latestStatus != TaskStatuses.Completed && task.Component.DueDate.Date <= DateTime.Today.AddDays(7),
                "Blocked" => latestStatus == TaskStatuses.Blocked || task.Component.Status == TaskStatuses.Blocked,
                "Completed" => latestStatus == TaskStatuses.Completed || task.Component.Status == TaskStatuses.Completed,
                _ => true
            };

            return matchesSearch && matchesFilter;
        }).ToList();

        TaskListBox.ItemsSource = visibleTasks;

        if (!string.IsNullOrWhiteSpace(selectedComponentId))
        {
            TaskListBox.SelectedItem = visibleTasks.FirstOrDefault(task => task.Component.Id == selectedComponentId);
        }
    }

    private void UpdateTaskSummary()
    {
        var today = DateTime.Today;
        var dueSoon = _tasks.Count(task =>
            task.Component.Status != TaskStatuses.Completed
            && task.Component.DueDate.Date >= today
            && task.Component.DueDate.Date <= today.AddDays(7));
        var blocked = _tasks.Count(task =>
            task.Component.Status == TaskStatuses.Blocked
            || task.LatestUpdate?.Status == TaskStatuses.Blocked);
        var completedToday = _tasks.Count(task =>
            task.LatestUpdate?.Status == TaskStatuses.Completed
            && task.LatestUpdate.UpdateDate.Date == today);

        AssignedCountText.Text = _tasks.Count.ToString();
        DueSoonCountText.Text = dueSoon.ToString();
        BlockedCountText.Text = blocked.ToString();
        CompletedTodayCountText.Text = completedToday.ToString();
        TaskProgressText.Text = _tasks.Count == 0
            ? "No assigned tasks right now"
            : $"{completedToday} of {_tasks.Count} tasks completed today";
    }

    private async void SaveUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedUpdateType = (UpdateTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? DailyUpdateTypes.AssignedTask;
        var status = (TaskStatusComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? TaskStatuses.InProgress;
        var description = DailyDescriptionTextBox.Text.Trim();
        var blockedReason = BlockedReasonTextBox.Text.Trim();

        if (string.Equals(selectedUpdateType, DailyUpdateTypes.SelfStudy, StringComparison.OrdinalIgnoreCase))
        {
            var selfStudyTopic = SelfStudyTopicTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(selfStudyTopic)) { MessageText.Text = "Enter the self study topic."; return; }
            if (string.IsNullOrWhiteSpace(description)) { MessageText.Text = "Describe what you studied today."; return; }
            if (status == TaskStatuses.Blocked && string.IsNullOrWhiteSpace(blockedReason)) { MessageText.Text = "Explain what is blocking this study session."; return; }
            if (MessageBox.Show("Save this self study update?", "Confirm self study update", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            try
            {
                var update = new DailyTaskUpdate
                {
                    UpdateType = DailyUpdateTypes.SelfStudy,
                    UserId = _userId,
                    UpdateDate = DateTime.Today,
                    Description = description,
                    SelfStudyTopic = selfStudyTopic,
                    Status = status,
                    BlockedReason = blockedReason
                };
                await Task.Run(() => _userService.SaveDailyTaskUpdate(update));
                await RefreshTaskListAndHistoryAsync();
                ClearUpdateInputs();
                MessageText.Text = "Today's self study update saved successfully.";
            }
            catch (Exception ex) { MessageText.Text = $"Unable to save today's update: {ex.Message}"; }
            return;
        }

        if (_selectedTask == null) { MessageText.Text = "Select a task first."; return; }
        if (string.IsNullOrWhiteSpace(description)) { MessageText.Text = "Describe the work completed today."; return; }
        if (status == TaskStatuses.Blocked && string.IsNullOrWhiteSpace(blockedReason)) { MessageText.Text = "Explain what is blocking this task."; return; }
        if (MessageBox.Show("Save today's task update?", "Confirm daily update", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            var update = new DailyTaskUpdate
            {
                UpdateType = DailyUpdateTypes.AssignedTask,
                ComponentId = _selectedTask.Component.Id,
                UserId = _userId,
                UpdateDate = DateTime.Today,
                Description = description,
                Status = status,
                BlockedReason = blockedReason
            };
            await Task.Run(() => _userService.SaveDailyTaskUpdate(update));
            await RefreshTaskListAndHistoryAsync();
            ClearUpdateInputs();
            MessageText.Text = "Today's update saved successfully.";
        }
        catch (Exception ex) { MessageText.Text = $"Unable to save today's update: {ex.Message}"; }
    }

    private void UpdateTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var isSelfStudy = (UpdateTypeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() == DailyUpdateTypes.SelfStudy;
        TaskListBox.Visibility = isSelfStudy ? Visibility.Collapsed : Visibility.Visible;
        SelfStudyTopicInputPanel.Visibility = isSelfStudy ? Visibility.Visible : Visibility.Collapsed;
        DailyDescriptionPlaceholderText.Text = isSelfStudy
            ? "Write the key points or notes from your study session..."
            : "Describe the work you completed on this assigned task...";

        if (isSelfStudy)
        {
            _selectedTask = null;
            TaskListBox.SelectedItem = null;
            ComponentTitleText.Text = "Self study session";
            ProjectText.Text = string.Empty;
            DescriptionText.Text = "Record what you studied today and the key points you want to remember.";
            DueDateText.Text = string.Empty;
        }
        else if (_selectedTask == null && _tasks.Count > 0)
        {
            TaskListBox.SelectedIndex = 0;
        }
    }

    private void ClearUpdateInputs()
    {
        DailyDescriptionTextBox.Text = string.Empty;
        SelfStudyTopicTextBox.Text = string.Empty;
        BlockedReasonTextBox.Text = string.Empty;
        TaskStatusComboBox.SelectedIndex = 1;
    }

    private async Task LoadDailyHistoryAsync()
    {
        var history = await Task.Run(() => _userService.GetMyDailyHistory(_userId));
        HistoryListView.ItemsSource = history
            .Select(update => new DailyHistoryDisplayRow
            {
                UpdateDate = update.UpdateDate,
                UpdateType = string.Equals(update.UpdateType, DailyUpdateTypes.SelfStudy, StringComparison.OrdinalIgnoreCase)
                    ? DailyUpdateTypes.SelfStudy
                    : DailyUpdateTypes.AssignedTask,
                Topic = string.IsNullOrWhiteSpace(update.SelfStudyTopic) ? "-" : update.SelfStudyTopic,
                Status = update.Status,
                Description = update.Description
            })
            .ToList();
    }

    private async Task RefreshTaskListAndHistoryAsync()
    {
        var selectedComponentId = _selectedTask?.Component.Id;

        ClearUpdateInputs();
        _tasks = await Task.Run(() => _userService.GetMyTasks(_userId));
        ApplyTaskView();
        UpdateTaskSummary();

        AssignedTask? refreshedSelectedTask = null;
        if (!string.IsNullOrWhiteSpace(selectedComponentId))
        {
            refreshedSelectedTask = _tasks.FirstOrDefault(task => task.Component.Id == selectedComponentId);
        }

        if (refreshedSelectedTask != null)
        {
            _selectedTask = refreshedSelectedTask;
            TaskListBox.SelectedItem = refreshedSelectedTask;
            var updates = await Task.Run(() => _userService.GetTaskUpdates(refreshedSelectedTask.Component.Id, _userId, false));

            var today = updates.FirstOrDefault(update => update.UpdateDate.Date == DateTime.Today);
            DailyDescriptionTextBox.Text = today?.Description ?? string.Empty;
            TaskStatusComboBox.SelectedIndex = Array.FindIndex(new[] { TaskStatuses.NotStarted, TaskStatuses.InProgress, TaskStatuses.Blocked, TaskStatuses.Completed }, status => status == (today?.Status ?? TaskStatuses.InProgress));
            BlockedReasonTextBox.Text = today?.BlockedReason ?? string.Empty;
            await LoadDailyHistoryAsync();
        }
        else
        {
            HistoryListView.ItemsSource = null;
            DailyDescriptionTextBox.Text = string.Empty;
            BlockedReasonTextBox.Text = string.Empty;
            TaskStatusComboBox.SelectedIndex = 1;
            await LoadDailyHistoryAsync();
        }

    }

    private sealed class DailyHistoryDisplayRow
    {
        public DateTime UpdateDate { get; init; }
        public string UpdateType { get; init; } = string.Empty;
        public string Topic { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
    }
}