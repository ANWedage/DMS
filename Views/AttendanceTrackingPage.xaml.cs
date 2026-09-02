using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using DMS.Helpers;
using DMS.Models;
using DMS.Services;

namespace DMS.Views
{
    public partial class AttendanceTrackingPage : Page
    {
        private readonly IUserService _userService;
        private readonly ObservableCollection<AttendanceRow> _rows = new();

        public AttendanceTrackingPage(IUserService userService)
        {
            InitializeComponent();
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

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadAttendanceAsync();
        }

        private async Task LoadPageAsync()
        {
            try
            {
                var settings = await Task.Run(_userService.GetMeetingSettings);
                MorningTimeTextBox.Text = settings.MorningTime;
                EveningTimeTextBox.Text = settings.EveningTime;
                MorningLinkTextBox.Text = settings.MorningMeetingLink;
                EveningLinkTextBox.Text = settings.EveningMeetingLink;
                await LoadAttendanceAsync();
            }
            catch (Exception ex)
            {
                SettingsMessageText.Text = $"Unable to load meeting settings: {ex.Message}";
            }
        }

        private async Task LoadAttendanceAsync()
        {
            try
            {
                var date = AttendanceDatePicker.SelectedDate ?? DateTime.Today;
                var usersTask = Task.Run(_userService.GetAllUsers);
                var attendanceTask = Task.Run(() => _userService.GetAllAttendance(date));
                await Task.WhenAll(usersTask, attendanceTask);

                var records = attendanceTask.Result;
                _rows.Clear();
                foreach (var user in usersTask.Result)
                {
                    foreach (var meetingType in new[] { MeetingTypes.Morning, MeetingTypes.Evening })
                    {
                        var record = records.FirstOrDefault(item => item.UserId == user.Id && item.MeetingType == meetingType);
                        _rows.Add(new AttendanceRow
                        {
                            RecordId = record?.Id ?? string.Empty,
                            MemberName = meetingType == MeetingTypes.Morning ? user.Username ?? user.Email : string.Empty,
                            MeetingType = meetingType,
                            Status = record?.Status ?? AttendanceStatuses.Pending,
                            MarkedByDisplay = record == null ? "-" : record.ChangedByAdminName ?? record.MarkedBy ?? "-",
                            AdminNote = record?.AdminNote ?? string.Empty
                        });
                    }
                }
                AttendanceGrid.ItemsSource = _rows;
                AttendanceMessageText.Text = string.Empty;
            }
            catch (Exception ex)
            {
                AttendanceMessageText.Text = $"Unable to load attendance: {ex.Message}";
            }
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
                var settings = new MeetingSettings
                {
                    MorningTime = MorningTimeTextBox.Text,
                    EveningTime = EveningTimeTextBox.Text,
                    MorningMeetingLink = MorningLinkTextBox.Text,
                    EveningMeetingLink = EveningLinkTextBox.Text,
                    TimeZoneId = _userService.GetMeetingSettings().TimeZoneId
                };
                await Task.Run(() => _userService.SaveMeetingSettings(
                    settings,
                    AppSession.CurrentUserId ?? string.Empty,
                    AppSession.CurrentDisplayName ?? AppSession.CurrentUsername ?? "Administrator"));
                SettingsMessageText.Foreground = System.Windows.Media.Brushes.DarkGreen;
                SettingsMessageText.Text = "Meeting settings saved.";
            }
            catch (Exception ex)
            {
                SettingsMessageText.Foreground = System.Windows.Media.Brushes.Firebrick;
                SettingsMessageText.Text = $"Unable to save meeting settings: {ex.Message}";
            }
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
                AttendanceStatuses.AbsentInformed
            };
        }
    }
}
