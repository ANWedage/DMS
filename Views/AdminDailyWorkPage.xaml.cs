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
            UpdateAttendanceSummary();
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
        TaskDescriptionTextBox.Text = _dailyTask?.Description ?? string.Empty;
        BlockedReasonTextBox.Text = _dailyTask?.BlockedReason ?? string.Empty;

        var fullDayLeave = _attendance.Count > 0
            && _attendance.All(record => record.Status == AttendanceStatuses.Leave);
        if (fullDayLeave)
        {
            TaskStatusComboBox.SelectedValue = null;
            TaskDescriptionTextBox.IsReadOnly = true;
            BlockedReasonTextBox.IsReadOnly = true;
            TaskStatusComboBox.IsEnabled = false;
            SubmitTaskButton.IsEnabled = false;
            TaskMessageText.Text = AdminDailyTaskStatuses.NotRequiredFullDayLeave;
            return;
        }

        TaskStatusComboBox.SelectedValue = null;
        foreach (var item in TaskStatusComboBox.Items.OfType<ComboBoxItem>())
            item.IsSelected = string.Equals(item.Content?.ToString(), _dailyTask?.Status, StringComparison.OrdinalIgnoreCase);
        if (!submitted)
            TaskStatusComboBox.SelectedIndex = 1;

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

    private async void MarkLeaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string meetingType })
            return;
        if (MessageBox.Show($"Mark your {meetingType} meeting as leave? You must still mark the other meeting present if attended.",
                "Confirm half-day leave", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            var date = SelectedDate;
            await Task.Run(() => _userService.MarkAdminAttendanceLeave(AdminId, meetingType, date));
            AttendanceMessageText.Text = $"{meetingType} half-day leave marked. Mark the attended meeting present manually.";
            await LoadPageAsync();
        }
        catch (Exception ex) { AttendanceMessageText.Text = ex.Message; }
    }

    private async void MarkFullDayLeaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Mark both meetings as full-day leave?", "Confirm full-day leave",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            var date = SelectedDate;
            await Task.Run(() => _userService.MarkAdminAttendanceFullDayLeave(AdminId, date));
            AttendanceMessageText.Text = "Full-day leave marked.";
            await LoadPageAsync();
        }
        catch (Exception ex) { AttendanceMessageText.Text = ex.Message; }
    }

    private void UpdateAttendanceSummary()
    {
        var statuses = _attendance.ToDictionary(record => record.MeetingType, record => record.Status);
        var morning = statuses.GetValueOrDefault(MeetingTypes.Morning, AttendanceStatuses.Pending);
        var evening = statuses.GetValueOrDefault(MeetingTypes.Evening, AttendanceStatuses.Pending);
        var summary = GetAttendanceSummary(morning, evening);
        AttendanceSummaryText.Text = $"Summary: {summary} (Morning: {morning}; Evening: {evening})";

        var canEditToday = SelectedDate.Date == DateTime.Today;
        var morningPending = morning == AttendanceStatuses.Pending;
        var eveningPending = evening == AttendanceStatuses.Pending;
        MorningHalfDayButton.IsEnabled = canEditToday && morningPending;
        EveningHalfDayButton.IsEnabled = canEditToday && eveningPending;
        FullDayLeaveButton.IsEnabled = canEditToday && morningPending && eveningPending;
        FullDayLeaveButton.ToolTip = FullDayLeaveButton.IsEnabled
            ? "Mark both pending meetings as leave"
            : "Full-day leave is available only while both meetings are pending";
    }

    private static string GetAttendanceSummary(string morning, string evening)
    {
        if (morning == AttendanceStatuses.Absent || evening == AttendanceStatuses.Absent)
            return AttendanceStatuses.Absent;
        if (morning == AttendanceStatuses.Leave && evening == AttendanceStatuses.Leave)
            return AttendanceStatuses.Leave;
        if ((morning == AttendanceStatuses.Present && evening == AttendanceStatuses.Leave)
            || (morning == AttendanceStatuses.Leave && evening == AttendanceStatuses.Present))
            return "Half day";
        if (morning == AttendanceStatuses.Present && evening == AttendanceStatuses.Present)
            return AttendanceStatuses.Present;
        return AttendanceStatuses.Pending;
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
            var attendanceSummaries = attendanceRows
                .GroupBy(row => new { row.AdminId, row.AdminName })
                .Select(group =>
                {
                    var statuses = group.ToDictionary(row => row.MeetingType, row => row.Status);
                    var morning = statuses.GetValueOrDefault(MeetingTypes.Morning, AttendanceStatuses.Pending);
                    var evening = statuses.GetValueOrDefault(MeetingTypes.Evening, AttendanceStatuses.Pending);
                    return new
                    {
                        group.Key.AdminName,
                        Morning = morning,
                        Evening = evening,
                        Summary = GetAttendanceSummary(morning, evening)
                    };
                })
                .ToList();
            Document.Create(document => document.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(30);
                page.Header().Column(column =>
                {
                    column.Item().Text("DMS Attendance and Task Submission Report").FontSize(20).Bold();
                    column.Item().Text("All Administrators").FontSize(12).SemiBold();
                    column.Item().Text($"Date: {date:yyyy-MM-dd}").FontSize(11);
                });
                page.Content().PaddingTop(18).Column(column =>
                {
                    column.Item().Text("Attendance").FontSize(15).Bold();
                    column.Item().PaddingTop(8).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.RelativeColumn(1.5f);
                            columns.RelativeColumn(1.5f);
                            columns.RelativeColumn(1.7f);
                        });
                        table.Header(header =>
                        {
                            header.Cell().Element(HeaderCell).Text("Administrator");
                            header.Cell().Element(HeaderCell).Text("Morning");
                            header.Cell().Element(HeaderCell).Text("Evening");
                            header.Cell().Element(HeaderCell).Text("Summary");
                        });
                        foreach (var row in attendanceSummaries)
                        {
                            table.Cell().Element(BodyCell).Text(row.AdminName);
                            table.Cell().Element(StatusCell(row.Morning)).Text(row.Morning);
                            table.Cell().Element(StatusCell(row.Evening)).Text(row.Evening);
                            table.Cell().Element(StatusCell(row.Summary)).Text(row.Summary);
                        }
                        if (attendanceSummaries.Count == 0)
                            table.Cell().ColumnSpan(4).Element(BodyCell).Text("No attendance records.");
                    });

                    column.Item().PaddingTop(20).Text("Daily Task Submissions").FontSize(15).Bold();
                    column.Item().PaddingTop(8).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2.5f);
                            columns.RelativeColumn(1.4f);
                            columns.RelativeColumn(5);
                            columns.RelativeColumn(3);
                        });
                        table.Header(header =>
                        {
                            header.Cell().Element(HeaderCell).Text("Administrator");
                            header.Cell().Element(HeaderCell).Text("Status");
                            header.Cell().Element(HeaderCell).Text("Work completed");
                            header.Cell().Element(HeaderCell).Text("Blocked reason");
                        });
                        foreach (var row in dailyTaskRows)
                        {
                            table.Cell().Element(BodyCell).Text(row.AdminName);
                            table.Cell().Element(StatusCell(row.Status)).Text(row.Status);
                            table.Cell().Element(BodyCell).Text(row.Description);
                            table.Cell().Element(BodyCell).Text(row.BlockedReason ?? string.Empty);
                        }
                        if (dailyTaskRows.Count == 0)
                            table.Cell().ColumnSpan(4).Element(BodyCell).Text("No administrators found.");
                    });
                });
                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Generated by ");
                    text.Span(AppSession.CurrentDisplayName ?? AppSession.CurrentUsername ?? "Administrator").Bold();
                });
            })).GeneratePdf(dialog.FileName);
            ReportMessageText.Text = $"All-admin PDF saved to {dialog.FileName}";
        }
        catch (Exception ex) { ReportMessageText.Text = $"Unable to generate all-admin PDF: {ex.Message}"; }
        finally
        {
            _isGeneratingReport = false;
        }
    }

    private static IContainer HeaderCell(IContainer container)
    {
        return container.Background(Colors.Grey.Darken2).Padding(5)
            .DefaultTextStyle(style => style.FontColor(Colors.White).Bold());
    }

    private static IContainer BodyCell(IContainer container)
    {
        return container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5);
    }

    private static Func<IContainer, IContainer> StatusCell(string status)
    {
        var color = status switch
        {
            AttendanceStatuses.Present or TaskStatuses.Completed => Colors.Green.Lighten3,
            AttendanceStatuses.Absent => Colors.Red.Lighten3,
            AttendanceStatuses.Leave => Colors.Purple.Lighten3,
            "Half day" => Colors.Blue.Lighten3,
            TaskStatuses.Blocked => Colors.Orange.Lighten3,
            "Not submitted" => Colors.Grey.Lighten3,
            _ => Colors.Grey.Lighten4
        };
        return container => container.Background(color).BorderBottom(1)
            .BorderColor(Colors.Grey.Lighten2).Padding(5);
    }

}