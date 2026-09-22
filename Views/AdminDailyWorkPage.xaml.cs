using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using DMS.Helpers;
using DMS.Models;
using DMS.Services;

namespace DMS.Views;

public partial class AdminDailyWorkPage : Page
{
    private readonly IUserService _userService;
    private List<AdminAttendanceRecord> _attendance = new();
    private AdminDailyTaskUpdate? _dailyTask;
    private bool _isLoading;
    private bool _isGeneratingReport;

    public AdminDailyWorkPage(IUserService userService)
    {
        InitializeComponent();
        _userService = userService;
        WorkDatePicker.SelectedDate = DateTime.Today;
        Loaded += async (_, _) => await LoadPageAsync();
    }

    private string AdminId => AppSession.CurrentUserId ?? string.Empty;
    private DateTime SelectedDate => WorkDatePicker.SelectedDate?.Date ?? DateTime.Today;

    private async void WorkDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
            await LoadPageAsync();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await LoadPageAsync();

    private async Task LoadPageAsync()
    {
        if (_isLoading || !AppSession.IsAdmin)
            return;

        _isLoading = true;
        try
        {
            var date = SelectedDate;
            var result = await Task.Run(() => (
                Attendance: _userService.GetAdminAttendance(AdminId, date),
                Task: _userService.GetAdminDailyTask(AdminId, date)));
            _attendance = result.Attendance;
            _dailyTask = result.Task;
            AttendanceList.ItemsSource = _attendance;
            PopulateTaskForm();
        }
        catch (Exception ex)
        {
            AttendanceList.ItemsSource = Array.Empty<AdminAttendanceRecord>();
            AttendanceMessageText.Text = $"Unable to load attendance: {ex.Message}";
            TaskMessageText.Text = $"Unable to load daily task: {ex.Message}";
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void PopulateTaskForm()
    {
        var submitted = _dailyTask != null;
        TaskStatusComboBox.SelectedValue = null;
        foreach (var item in TaskStatusComboBox.Items.OfType<ComboBoxItem>())
            item.IsSelected = string.Equals(item.Content?.ToString(), _dailyTask?.Status, StringComparison.OrdinalIgnoreCase);
        if (!submitted)
            TaskStatusComboBox.SelectedIndex = 1;

        TaskDescriptionTextBox.Text = _dailyTask?.Description ?? string.Empty;
        BlockedReasonTextBox.Text = _dailyTask?.BlockedReason ?? string.Empty;
        TaskDescriptionTextBox.IsReadOnly = submitted;
        BlockedReasonTextBox.IsReadOnly = submitted;
        TaskStatusComboBox.IsEnabled = !submitted;
        SubmitTaskButton.IsEnabled = !submitted;
        TaskMessageText.Text = submitted ? "Daily task already submitted for this date." : string.Empty;
    }

    private async void MarkAttendanceButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string meetingType)
            return;
        if (MessageBox.Show($"Mark your {meetingType} attendance as present?", "Confirm attendance", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            var date = SelectedDate;
            await Task.Run(() => _userService.MarkAdminAttendancePresent(AdminId, meetingType, date));
            AttendanceMessageText.Text = $"{meetingType} attendance marked present.";
            await LoadPageAsync();
        }
        catch (Exception ex) { AttendanceMessageText.Text = ex.Message; }
    }

    private async void SubmitTaskButton_Click(object sender, RoutedEventArgs e)
    {
        var description = TaskDescriptionTextBox.Text.Trim();
        var status = (TaskStatusComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? TaskStatuses.InProgress;
        var blockedReason = BlockedReasonTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(description)) { TaskMessageText.Text = "Enter the work completed."; return; }
        if (status == TaskStatuses.Blocked && string.IsNullOrWhiteSpace(blockedReason)) { TaskMessageText.Text = "Enter the blocked reason."; return; }
        if (MessageBox.Show("Submit this admin daily task?", "Confirm daily task", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            var date = SelectedDate;
            await Task.Run(() => _userService.SaveAdminDailyTask(new AdminDailyTaskUpdate
            {
                AdminId = AdminId,
                UpdateDate = date,
                Description = description,
                Status = status,
                BlockedReason = blockedReason
            }));
            TaskMessageText.Text = "Daily task submitted successfully.";
            await LoadPageAsync();
        }
        catch (Exception ex) { TaskMessageText.Text = $"Unable to submit daily task: {ex.Message}"; }
    }

    private async void AllAdminCombinedReportButton_Click(object sender, RoutedEventArgs e) => await GenerateAllAdminReportAsync("combined");

    private async Task GenerateAllAdminReportAsync(string reportType)
    {
        if (_isGeneratingReport)
            return;
        var date = SelectedDate;
        if (MessageBox.Show($"Generate the all-admin {reportType} PDF for {date:yyyy-MM-dd}?", "Confirm PDF report", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _isGeneratingReport = true;
        try
        {
            var includeAttendance = reportType is "attendance" or "combined";
            var includeTask = reportType is "task" or "combined";
            var attendanceTask = includeAttendance
                ? Task.Run(() => _userService.GetAllAdminAttendance(date))
                : Task.FromResult(new List<AdminAttendanceReportRow>());
            var dailyTaskTask = includeTask
                ? Task.Run(() => _userService.GetAllAdminDailyTasks(date))
                : Task.FromResult(new List<AdminDailyTaskReportRow>());
            await Task.WhenAll(attendanceTask, dailyTaskTask);

            var dialog = new SaveFileDialog
            {
                Title = "Save all-admin work report",
                Filter = "PDF files (*.pdf)|*.pdf",
                FileName = $"Attendance-and-Task-Submission-Report-{date:yyyy-MM-dd}.pdf",
                AddExtension = true,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog() != true)
                return;

            QuestPDF.Settings.License = LicenseType.Community;
            var attendanceRows = attendanceTask.Result;
            var dailyTaskRows = dailyTaskTask.Result;
            Document.Create(document => document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.Header().Column(column =>
                {
                    column.Item().Text("Attendance and Task Submission Report").FontSize(20).Bold();
                    column.Item().Text($"Date: {date:yyyy-MM-dd}").FontSize(11);
                });
                page.Content().PaddingTop(20).Column(column =>
                {
                    if (includeAttendance)
                    {
                        column.Item().Text("Attendance").FontSize(15).Bold();
                        foreach (var row in attendanceRows)
                            column.Item().PaddingTop(6).Text($"{row.AdminName} - {row.MeetingType}: {row.Status}");
                        if (attendanceRows.Count == 0) column.Item().PaddingTop(6).Text("No attendance records.");
                    }
                    if (includeTask)
                    {
                        column.Item().PaddingTop(18).Text("Daily Tasks").FontSize(15).Bold();
                        foreach (var row in dailyTaskRows)
                        {
                            column.Item().PaddingTop(8).Text($"{row.AdminName} - {row.Status}").Bold();
                            column.Item().PaddingTop(3).Text(row.Description);
                            if (!string.IsNullOrWhiteSpace(row.BlockedReason))
                                column.Item().PaddingTop(3).Text($"Blocked reason: {row.BlockedReason}");
                        }
                        if (dailyTaskRows.Count == 0) column.Item().PaddingTop(6).Text("No administrators found.");
                    }
                });
                page.Footer().AlignCenter().Text("Generated by DMS");
            })).GeneratePdf(dialog.FileName);
            ReportMessageText.Text = $"All-admin PDF saved to {dialog.FileName}";
        }
        catch (Exception ex) { ReportMessageText.Text = $"Unable to generate all-admin PDF: {ex.Message}"; }
        finally
        {
            _isGeneratingReport = false;
        }
    }

}