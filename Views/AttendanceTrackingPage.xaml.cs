using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using DMS.Helpers;
using DMS.Models;
using DMS.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using VisualTreeHelper = System.Windows.Media.VisualTreeHelper;

namespace DMS.Views
{
    public partial class AttendanceTrackingPage : Page
    {
        private readonly IUserService _userService;
        private readonly List<DeveloperAttendanceRow> _rows = new();
        private int _activeLoadCount;

        public AttendanceTrackingPage(IUserService userService)
        {
            InitializeComponent();
            InitializeTimePickers();
            _userService = userService;
            AttendanceDatePicker.SelectedDate = DateTime.Today;
            Loaded += AttendanceTrackingPage_Loaded;
        }

        private async void AttendanceTrackingPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadPageAsync();
        }

        private async void AttendanceDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded)
                await LoadAttendanceAsync();
        }

        private void PositionFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded)
                ApplyPositionFilter();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadPageAsync();
        }

        private void Page_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (IsWithinElement(e.OriginalSource as DependencyObject, AttendanceGrid))
            {
                var gridScrollViewer = FindVisualChild<ScrollViewer>(AttendanceGrid);
                if (gridScrollViewer is not null && gridScrollViewer.ScrollableHeight > 0)
                    return;
            }

            PageScrollViewer.ScrollToVerticalOffset(PageScrollViewer.VerticalOffset - e.Delta);
            e.Handled = true;
        }

        private static bool IsWithinElement(DependencyObject? source, DependencyObject element)
        {
            while (source is not null)
            {
                if (ReferenceEquals(source, element))
                    return true;

                source = source is System.Windows.Media.Visual
                    ? VisualTreeHelper.GetParent(source)
                    : LogicalTreeHelper.GetParent(source);
            }

            return false;
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is T match)
                    return match;

                var descendant = FindVisualChild<T>(child);
                if (descendant is not null)
                    return descendant;
            }

            return null;
        }

        private void InitializeTimePickers()
        {
            var hours = Enumerable.Range(0, 24).Select(value => value.ToString("D2")).ToList();
            var minutes = Enumerable.Range(0, 60).Select(value => value.ToString("D2")).ToList();

            foreach (var picker in new[]
            {
                FullStackMorningHourBox, FullStackEveningHourBox,
                QaMorningHourBox, QaEveningHourBox,
                UiUxMorningHourBox, UiUxEveningHourBox,
                WeeklyHourBox
            })
                picker.ItemsSource = hours;

            foreach (var picker in new[]
            {
                FullStackMorningMinuteBox, FullStackEveningMinuteBox,
                QaMorningMinuteBox, QaEveningMinuteBox,
                UiUxMorningMinuteBox, UiUxEveningMinuteBox,
                WeeklyMinuteBox
            })
                picker.ItemsSource = minutes;

        }

        private static void SetTime(ComboBox hourPicker, ComboBox minutePicker, string value)
        {
            if (!TimeSpan.TryParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture, out var time))
                time = new TimeSpan(10, 0, 0);

            hourPicker.SelectedItem = time.Hours.ToString("D2");
            minutePicker.SelectedItem = time.Minutes.ToString("D2");
        }

        private static string GetTime(ComboBox hourPicker, ComboBox minutePicker)
        {
            var hour = hourPicker.SelectedItem?.ToString() ?? "10";
            var minute = minutePicker.SelectedItem?.ToString() ?? "00";
            return $"{hour}:{minute}";
        }

        private void GenerateReportButton_Click(object sender, RoutedEventArgs e)
        {
            var date = AttendanceDatePicker.SelectedDate ?? DateTime.Today;
            var confirmation = MessageBox.Show(
                $"Generate the attendance PDF report for {date:yyyy-MM-dd}?",
                "Confirm PDF report",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirmation != MessageBoxResult.Yes)
                return;

            var dialog = new SaveFileDialog
            {
                Title = "Save daily attendance report",
                Filter = "PDF files (*.pdf)|*.pdf",
                FileName = $"Attendance-{date:yyyy-MM-dd}.pdf",
                AddExtension = true,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog() != true)
                return;

            try
            {
                QuestPDF.Settings.License = LicenseType.Community;
                Document.Create(document => document.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(30);
                    page.Header().Column(column =>
                    {
                        column.Item().Text("DMS Daily Attendance Report").FontSize(20).Bold();
                        column.Item().Text($"Date: {date:yyyy-MM-dd}").FontSize(11);
                    });
                    page.Content().PaddingTop(18).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(1.2f);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(1.5f);
                            columns.RelativeColumn(1.2f);
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(2.5f);
                            columns.RelativeColumn(1.5f);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(HeaderCell).Text("Team");
                            header.Cell().Element(HeaderCell).Text("Member");
                            header.Cell().Element(HeaderCell).Text("Meeting");
                            header.Cell().Element(HeaderCell).Text("Status");
                            header.Cell().Element(HeaderCell).Text("Marked by");
                            header.Cell().Element(HeaderCell).Text("Admin note");
                            header.Cell().Element(HeaderCell).Text("Daily task");
                        });

                        foreach (var developerRow in GetFilteredAttendanceRows())
                        {
                            var rowSpan = (uint)developerRow.AttendanceRows.Count;
                            table.Cell().RowSpan(rowSpan).Element(BodyCell).Text(developerRow.Team);
                            table.Cell().RowSpan(rowSpan).Element(BodyCell).Text(developerRow.MemberName);

                            var firstAttendanceRow = developerRow.AttendanceRows[0];
                            table.Cell().Element(BodyCell).Text(firstAttendanceRow.MeetingType);
                            table.Cell().Element(StatusCell(firstAttendanceRow.Status)).Text(firstAttendanceRow.Status);
                            table.Cell().Element(BodyCell).Text(firstAttendanceRow.MarkedByDisplay);
                            table.Cell().Element(BodyCell).Text(firstAttendanceRow.AdminNote);
                            table.Cell().RowSpan(rowSpan).Element(DailyTaskCell(developerRow.DailyTaskDisplayStatus))
                                .Text(developerRow.DailyTaskDisplayStatus);

                            for (var index = 1; index < developerRow.AttendanceRows.Count; index++)
                            {
                                var attendanceRow = developerRow.AttendanceRows[index];
                                table.Cell().Element(BodyCell).Text(attendanceRow.MeetingType);
                                table.Cell().Element(StatusCell(attendanceRow.Status)).Text(attendanceRow.Status);
                                table.Cell().Element(BodyCell).Text(attendanceRow.MarkedByDisplay);
                                table.Cell().Element(BodyCell).Text(attendanceRow.AdminNote);
                            }
                        }
                    });
                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.Span("Generated by ");
                        text.Span(AppSession.CurrentDisplayName ?? AppSession.CurrentUsername ?? "Administrator").Bold();
                    });
                })).GeneratePdf(dialog.FileName);

                AttendanceMessageText.Text = string.Empty;
                MessageBox.Show($"Attendance PDF report saved to:\n{dialog.FileName}",
                    "Report saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AttendanceMessageText.Foreground = System.Windows.Media.Brushes.Firebrick;
                AttendanceMessageText.Text = $"Unable to generate PDF report: {ex.Message}";
            }
        }

        private static IContainer HeaderCell(IContainer container)
        {
            return container.Background(Colors.Grey.Darken2).Padding(5).DefaultTextStyle(style => style.FontColor(Colors.White).Bold());
        }

        private static IContainer BodyCell(IContainer container)
        {
            return container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5);
        }

        private static Func<IContainer, IContainer> StatusCell(string status)
        {
            var color = status switch
            {
                AttendanceStatuses.Present => Colors.Green.Lighten3,
                AttendanceStatuses.Absent => Colors.Red.Lighten3,
                AttendanceStatuses.AbsentInformed => Colors.Orange.Lighten3,
                AttendanceStatuses.Leave => Colors.Purple.Lighten3,
                _ => Colors.Grey.Lighten3
            };
            return container => container.Background(color).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5);
        }

        private static Func<IContainer, IContainer> DailyTaskCell(string status)
        {
            var color = status switch
            {
                "Submitted" => Colors.Green.Lighten4,
                "Leave" => Colors.Purple.Lighten4,
                "Half day" => Colors.Orange.Lighten4,
                _ => Colors.Red.Lighten4
            };
            return container => container.Background(color).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5);
        }

        private async Task LoadPageAsync()
        {
            BeginLoading();
            try
            {
                // Load settings and attendance data in parallel for better performance
                var settingsTask = Task.Run(_userService.GetMeetingSettings);
                var attendanceTask = LoadAttendanceAsync(settingsTask, showLoadingIndicator: false);
                
                await Task.WhenAll(settingsTask, attendanceTask);
                
                // Populate settings textboxes after both tasks complete
                var settings = settingsTask.Result;
                settings.EnsureTeamSettings();
                SetTime(FullStackMorningHourBox, FullStackMorningMinuteBox, settings.FullStack!.MorningTime);
                FullStackMorningLinkTextBox.Text = settings.FullStack.MorningMeetingLink;
                FullStackMorningJoinButton.IsEnabled = !string.IsNullOrWhiteSpace(settings.FullStack.MorningMeetingLink);
                SetTime(FullStackEveningHourBox, FullStackEveningMinuteBox, settings.FullStack.EveningTime);
                FullStackEveningLinkTextBox.Text = settings.FullStack.EveningMeetingLink;
                FullStackEveningJoinButton.IsEnabled = !string.IsNullOrWhiteSpace(settings.FullStack.EveningMeetingLink);
                SetTime(QaMorningHourBox, QaMorningMinuteBox, settings.QA!.MorningTime);
                QaMorningLinkTextBox.Text = settings.QA.MorningMeetingLink;
                QaMorningJoinButton.IsEnabled = !string.IsNullOrWhiteSpace(settings.QA.MorningMeetingLink);
                SetTime(QaEveningHourBox, QaEveningMinuteBox, settings.QA.EveningTime);
                QaEveningLinkTextBox.Text = settings.QA.EveningMeetingLink;
                QaEveningJoinButton.IsEnabled = !string.IsNullOrWhiteSpace(settings.QA.EveningMeetingLink);
                SetTime(UiUxMorningHourBox, UiUxMorningMinuteBox, settings.UIUX!.MorningTime);
                UiUxMorningLinkTextBox.Text = settings.UIUX.MorningMeetingLink;
                UiUxMorningJoinButton.IsEnabled = !string.IsNullOrWhiteSpace(settings.UIUX.MorningMeetingLink);
                SetTime(UiUxEveningHourBox, UiUxEveningMinuteBox, settings.UIUX.EveningTime);
                UiUxEveningLinkTextBox.Text = settings.UIUX.EveningMeetingLink;
                UiUxEveningJoinButton.IsEnabled = !string.IsNullOrWhiteSpace(settings.UIUX.EveningMeetingLink);
                SetTime(WeeklyHourBox, WeeklyMinuteBox, settings.WeeklyTime);
                WeeklyLinkTextBox.Text = settings.WeeklyMeetingLink;
                WeeklyJoinButton.IsEnabled = !string.IsNullOrWhiteSpace(settings.WeeklyMeetingLink);
                UpdateLastSettingsText(settings);
            }
            catch (Exception ex)
            {
                SettingsMessageText.Text = $"Unable to load meeting settings: {ex.Message}";
            }
            finally
            {
                EndLoading();
            }
        }

        private void JoinMeetingButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string meetingKey })
                return;

            var link = meetingKey switch
            {
                "FullStackMorning" => FullStackMorningLinkTextBox.Text,
                "FullStackEvening" => FullStackEveningLinkTextBox.Text,
                "QaMorning" => QaMorningLinkTextBox.Text,
                "QaEvening" => QaEveningLinkTextBox.Text,
                "UiUxMorning" => UiUxMorningLinkTextBox.Text,
                "UiUxEvening" => UiUxEveningLinkTextBox.Text,
                "Weekly" => WeeklyLinkTextBox.Text,
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(link))
            {
                SettingsMessageText.Text = "Enter and save a Google Meet link before joining.";
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(link.Trim()) { UseShellExecute = true });
                SettingsMessageText.Text = string.Empty;
            }
            catch (Exception ex)
            {
                SettingsMessageText.Text = $"Unable to open the Google Meet link: {ex.Message}";
            }
        }

        private async Task LoadAttendanceAsync(Task<MeetingSettings>? sharedSettingsTask = null, bool showLoadingIndicator = true)
        {
            if (showLoadingIndicator)
                BeginLoading();

            try
            {
                var date = AttendanceDatePicker.SelectedDate ?? DateTime.Today;
                var usersTask = Task.Run(_userService.GetAllUsers);
                var attendanceTask = Task.Run(() => _userService.GetAllAttendance(date));
                var dailyTaskStatusesTask = Task.Run(() => _userService.GetDeveloperDailyTaskStatus(date));
                var settingsTask = sharedSettingsTask ?? Task.Run(_userService.GetMeetingSettings);
                await Task.WhenAll(usersTask, attendanceTask, dailyTaskStatusesTask, settingsTask);

                var records = attendanceTask.Result;
                var dailyTaskStatuses = dailyTaskStatusesTask.Result.ToDictionary(
                    status => status.UserId,
                    status => status.DisplayStatus,
                    StringComparer.Ordinal);
                var recordsByUserAndMeeting = records.ToDictionary(
                    item => (item.UserId, item.MeetingType));
                _rows.Clear();

                if (!MeetingSchedule.IsWorkingDay(date))
                {
                    ApplyPositionFilter();
                    AttendanceMessageText.Text = "Weekend: non-working day. No attendance records are required.";
                    return;
                }

                foreach (var user in usersTask.Result
                    .OrderBy(user => string.IsNullOrWhiteSpace(user.Position) ? 1 : 0)
                    .ThenBy(user => user.Position)
                    .ThenBy(user => user.Username ?? user.Email))
                {
                    var userRecords = records.Where(record => record.UserId == user.Id).ToList();
                    var historicalTeam = userRecords
                        .Select(record => record.Team)
                        .FirstOrDefault(User.IsSupportedPosition);
                    var schedulePosition = historicalTeam ?? user.Position;
                    var userSlots = MeetingSchedule.ForDate(settingsTask.Result, date, schedulePosition);
                    var dailyTaskStatus = dailyTaskStatuses.GetValueOrDefault(user.Id, "Not submitted");
                    if (userSlots.Count == 0)
                    {
                        _rows.Add(new DeveloperAttendanceRow
                        {
                            MemberName = user.Username ?? user.Email,
                            Team = "Unassigned",
                            Position = "Unassigned",
                            DailyTaskDisplayStatus = dailyTaskStatus,
                            AttendanceRows = new List<AttendanceRow>
                            {
                                new()
                                {
                                    MemberName = user.Username ?? user.Email,
                                    MeetingType = "No meetings configured",
                                    Status = "-",
                                    MarkedByDisplay = "-",
                                    AdminNote = "Assign Full Stack, QA, or UI/UX"
                                }
                            }
                        });
                        continue;
                    }

                    var developerRow = new DeveloperAttendanceRow
                    {
                        Team = schedulePosition,
                        MemberName = user.Username ?? user.Email,
                        Position = schedulePosition,
                        DailyTaskDisplayStatus = dailyTaskStatus
                    };
                    for (var slotIndex = 0; slotIndex < userSlots.Count; slotIndex++)
                    {
                        var slot = userSlots[slotIndex];
                        recordsByUserAndMeeting.TryGetValue((user.Id, slot.Type), out var record);
                        developerRow.AttendanceRows.Add(new AttendanceRow
                        {
                            RecordId = record?.Id ?? string.Empty,
                            MemberName = user.Username ?? user.Email,
                            MeetingType = slot.DisplayName,
                            Status = record?.Status ?? AttendanceStatuses.Pending,
                            MarkedByDisplay = record == null ? "-" : record.ChangedByAdminName ?? record.MarkedBy ?? "-",
                            AdminNote = record?.AdminNote ?? string.Empty
                        });
                    }
                    _rows.Add(developerRow);
                }
                ApplyPositionFilter();
                AttendanceMessageText.Text = string.Empty;
            }
            catch (Exception ex)
            {
                AttendanceMessageText.Text = $"Unable to load attendance: {ex.Message}";
            }
            finally
            {
                if (showLoadingIndicator)
                    EndLoading();
            }
        }

        private void BeginLoading()
        {
            _activeLoadCount++;
            LoadingOverlay.Visibility = Visibility.Visible;
            RefreshButton.IsEnabled = false;
            AttendanceDatePicker.IsEnabled = false;
            PositionFilterComboBox.IsEnabled = false;
        }

        private void EndLoading()
        {
            _activeLoadCount = Math.Max(0, _activeLoadCount - 1);
            if (_activeLoadCount > 0)
                return;

            LoadingOverlay.Visibility = Visibility.Collapsed;
            RefreshButton.IsEnabled = true;
            AttendanceDatePicker.IsEnabled = true;
            PositionFilterComboBox.IsEnabled = true;
        }

        private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var confirmation = MessageBox.Show(
                "Save the updated meeting times and Google Meet links?",
                "Confirm meeting settings",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirmation != MessageBoxResult.Yes)
                return;

            try
            {
                var existingSettings = await Task.Run(_userService.GetMeetingSettings);
                var settings = new MeetingSettings
                {
                    WeeklyTime = GetTime(WeeklyHourBox, WeeklyMinuteBox),
                    WeeklyMeetingLink = WeeklyLinkTextBox.Text,
                    LeaveFormLink = existingSettings.LeaveFormLink,
                    TimeZoneId = existingSettings.TimeZoneId,
                    FullStack = new TeamMeetingSettings
                    {
                        MorningTime = GetTime(FullStackMorningHourBox, FullStackMorningMinuteBox),
                        MorningMeetingLink = FullStackMorningLinkTextBox.Text,
                        EveningTime = GetTime(FullStackEveningHourBox, FullStackEveningMinuteBox),
                        EveningMeetingLink = FullStackEveningLinkTextBox.Text
                    },
                    QA = new TeamMeetingSettings
                    {
                        MorningTime = GetTime(QaMorningHourBox, QaMorningMinuteBox),
                        MorningMeetingLink = QaMorningLinkTextBox.Text,
                        EveningTime = GetTime(QaEveningHourBox, QaEveningMinuteBox),
                        EveningMeetingLink = QaEveningLinkTextBox.Text
                    },
                    UIUX = new TeamMeetingSettings
                    {
                        MorningTime = GetTime(UiUxMorningHourBox, UiUxMorningMinuteBox),
                        MorningMeetingLink = UiUxMorningLinkTextBox.Text,
                        EveningTime = GetTime(UiUxEveningHourBox, UiUxEveningMinuteBox),
                        EveningMeetingLink = UiUxEveningLinkTextBox.Text
                    }
                };
                settings.MorningTime = settings.FullStack.MorningTime;
                settings.MorningMeetingLink = settings.FullStack.MorningMeetingLink;
                settings.EveningTime = settings.FullStack.EveningTime;
                settings.EveningMeetingLink = settings.FullStack.EveningMeetingLink;
                await Task.Run(() => _userService.SaveMeetingSettings(
                    settings,
                    AppSession.CurrentUserId ?? string.Empty,
                    AppSession.CurrentDisplayName ?? AppSession.CurrentUsername ?? "Administrator"));

                await LoadPageAsync();

                LastSettingsUpdateText.Text = $"Last updated by {AppSession.CurrentDisplayName ?? AppSession.CurrentUsername ?? "Administrator"} on {DateTime.Now:g}";
                SettingsMessageText.Text = string.Empty;
                MessageBox.Show("Meeting details saved successfully.",
                    "Meeting details saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                SettingsMessageText.Foreground = System.Windows.Media.Brushes.Firebrick;
                SettingsMessageText.Text = $"Unable to save meeting settings: {ex.Message}";
            }
        }

        private void UpdateLastSettingsText(MeetingSettings settings)
        {
            LastSettingsUpdateText.Text = string.IsNullOrWhiteSpace(settings.UpdatedByAdminName)
                ? "Meeting settings have not been updated yet."
                : $"Last updated by {settings.UpdatedByAdminName} on {settings.UpdatedAt.ToLocalTime():g}";
        }

        private void ApplyPositionFilter()
        {
            var filteredRows = GetFilteredAttendanceRows().ToList();
            AttendanceGrid.ItemsSource = filteredRows;
            AttendanceCountText.Text = $"Developers: {filteredRows.Count}";
        }

        private IEnumerable<DeveloperAttendanceRow> GetFilteredAttendanceRows()
        {
            var selectedPosition = (PositionFilterComboBox?.SelectedItem as ComboBoxItem)?.Content as string;
            return _rows.Where(row =>
                string.IsNullOrWhiteSpace(selectedPosition)
                || selectedPosition == "All positions"
                || (selectedPosition == "Unassigned"
                    ? string.IsNullOrWhiteSpace(row.Position) || row.Position == "Unassigned"
                    : string.Equals(row.Position, selectedPosition, StringComparison.OrdinalIgnoreCase)));
        }

        private async void SaveAttendanceButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: AttendanceRow row } || string.IsNullOrWhiteSpace(row.RecordId))
            {
                AttendanceMessageText.Text = "This member has no attendance record because the account is inactive.";
                return;
            }

            var confirmation = MessageBox.Show(
                $"Save {row.MeetingType} attendance for {row.MemberName} as {row.Status}?",
                "Confirm attendance update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirmation != MessageBoxResult.Yes)
                return;

            var saved = await Task.Run(() => _userService.UpdateAttendanceStatus(
                row.RecordId,
                row.Status,
                AppSession.CurrentUserId ?? string.Empty,
                AppSession.CurrentDisplayName ?? AppSession.CurrentUsername ?? "Administrator",
                row.AdminNote));
            AttendanceMessageText.Text = saved ? "Attendance updated." : "Attendance could not be updated.";
            if (saved)
                await LoadAttendanceAsync();
        }

        private sealed class DeveloperAttendanceRow
        {
            public string Team { get; init; } = string.Empty;
            public string MemberName { get; init; } = string.Empty;
            public string Position { get; init; } = string.Empty;
            public string DailyTaskDisplayStatus { get; init; } = "Not submitted";
            public List<AttendanceRow> AttendanceRows { get; init; } = new();
        }

        private sealed class AttendanceRow
        {
            public string RecordId { get; init; } = string.Empty;
            public string MemberName { get; init; } = string.Empty;
            public string MeetingType { get; init; } = string.Empty;
            public string MarkedByDisplay { get; init; } = string.Empty;
            public string Status { get; set; } = AttendanceStatuses.Pending;
            public string AdminNote { get; set; } = string.Empty;
            public List<string> StatusOptions { get; } = new()
            {
                AttendanceStatuses.Present,
                AttendanceStatuses.Absent,
                AttendanceStatuses.AbsentInformed,
                AttendanceStatuses.Leave
            };
        }
    }
}
