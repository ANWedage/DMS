using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using DMS.Models;
using DMS.Services;

namespace DMS.Views
{
    public partial class AttendancePage : Page
    {
        private readonly IUserService _userService;
        private readonly string _userId;
        private MeetingSettings _settings = new();
        private string _position = string.Empty;

        public AttendancePage(IUserService userService, string userId)
        {
            InitializeComponent();
            _userService = userService;
            _userId = userId;
            AttendanceDatePicker.SelectedDate = DateTime.Today;
            Loaded += AttendancePage_Loaded;
        }

        private async void AttendancePage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadAttendanceAsync();
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

        private async Task LoadAttendanceAsync()
        {
            try
            {
                var date = AttendanceDatePicker.SelectedDate ?? DateTime.Today;
                
                // Execute both operations in parallel for better performance
                var settingsTask = Task.Run(() => _userService.GetMeetingSettingsForUser(_userId));
                var attendanceTask = Task.Run(() => _userService.GetUserAttendance(_userId, date));
                var userTask = Task.Run(() => _userService.GetUserById(_userId));
                
                await Task.WhenAll(settingsTask, attendanceTask, userTask);
                
                _settings = settingsTask.Result;
                _position = userTask.Result.Position;
                var records = attendanceTask.Result;

                if (!MeetingSchedule.IsWorkingDay(date))
                {
                    AttendanceItems.ItemsSource = null;
                    MessageText.Text = "Weekend: non-working day. Attendance is not required.";
                    return;
                }

                if (!User.IsSupportedPosition(_position))
                {
                    AttendanceItems.ItemsSource = null;
                    MessageText.Text = "Your team has not been assigned yet. Please contact an administrator.";
                    return;
                }
                
                var rows = MeetingSchedule.ForDate(_settings, date, _position)
                    .Select(slot => CreateRow(records, slot, date))
                    .ToArray();
                AttendanceItems.ItemsSource = rows;
                MessageText.Text = string.Empty;
            }
            catch (Exception ex)
            {
                AttendanceItems.ItemsSource = null;
                MessageText.Text = $"Unable to load attendance: {ex.Message}";
            }
        }

        private AttendanceRow CreateRow(List<AttendanceRecord> records, MeetingSlot slot, DateTime date)
        {
            var record = records.FirstOrDefault(item => item.MeetingType == slot.Type)
                ?? new AttendanceRecord { MeetingType = slot.Type, Status = AttendanceStatuses.Pending };
            var now = GetApplicationNow();
            var start = ParseMeetingStart(slot.Type, slot.Time, date.Date);
            return new AttendanceRow
            {
                RecordId = record.Id,
                MeetingType = slot.Type,
                MeetingDisplayName = slot.DisplayName,
                MeetingTime = slot.Time,
                MeetingLink = slot.Link,
                Status = record.Status,
                CanMarkPresent = record.Status == AttendanceStatuses.Pending
                    && now.Date == date.Date
                    && now >= start && now <= start.AddMinutes(15)
            };
        }

        private void JoinMeetingButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string link } || string.IsNullOrWhiteSpace(link))
            {
                MessageText.Text = "The Google Meet link has not been configured yet.";
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageText.Text = $"Unable to open the Google Meet link: {ex.Message}";
            }
        }

        private async void MarkPresentButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string meetingType })
                return;

            var confirmation = MessageBox.Show(
                $"Confirm that you attended the {(meetingType == MeetingTypes.Weekly ? "Weekly Meeting" : $"{meetingType} Meeting")}?",
                "Confirm attendance",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirmation != MessageBoxResult.Yes)
                return;

            try
            {
                var date = AttendanceDatePicker.SelectedDate ?? DateTime.Today;
                var marked = await Task.Run(() => _userService.MarkAttendancePresent(_userId, meetingType, date));
                if (!marked)
                {
                    MessageText.Text = "Attendance could not be marked. Check the meeting time window.";
                    return;
                }

                var rows = AttendanceItems.ItemsSource?.Cast<AttendanceRow>().ToArray();
                var row = rows?.FirstOrDefault(item => item.MeetingType == meetingType);
                if (row != null)
                {
                    row.Status = AttendanceStatuses.Present;
                    row.CanMarkPresent = false;
                    AttendanceItems.ItemsSource = rows;
                }

                MessageText.Text = "Attendance marked as Present.";
            }
            catch (Exception ex)
            {
                MessageText.Text = $"Unable to mark attendance: {ex.Message}";
            }
        }

        private DateTime GetApplicationNow()
        {
            var configuredTimeZone = _settings.TimeZoneId?.Trim();
            if (string.IsNullOrWhiteSpace(configuredTimeZone)
                || string.Equals(configuredTimeZone, "Sri Lanka Standard Time", StringComparison.OrdinalIgnoreCase)
                || string.Equals(configuredTimeZone, "Asia/Colombo", StringComparison.OrdinalIgnoreCase))
                return DateTime.UtcNow.AddHours(5.5);

            try
            {
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
                    TimeZoneInfo.FindSystemTimeZoneById(configuredTimeZone));
            }
            catch (TimeZoneNotFoundException)
            {
                return DateTime.UtcNow.AddHours(5.5);
            }
            catch (InvalidTimeZoneException)
            {
                return DateTime.UtcNow.AddHours(5.5);
            }
        }

        private static DateTime ParseMeetingStart(string meetingType, string value, DateTime date)
        {
            return date.Add(TimeSpan.TryParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture, out var time)
                ? time
                : meetingType is MeetingTypes.Morning or MeetingTypes.Weekly
                    ? new TimeSpan(10, 0, 0)
                    : new TimeSpan(17, 0, 0));
        }

        private sealed class AttendanceRow
        {
            public string RecordId { get; init; } = string.Empty;
            public string MeetingType { get; init; } = string.Empty;
            public string MeetingDisplayName { get; init; } = string.Empty;
            public string MeetingTime { get; init; } = string.Empty;
            public string MeetingLink { get; init; } = string.Empty;
            public string Status { get; set; } = AttendanceStatuses.Pending;
            public bool CanMarkPresent { get; set; }
        }
    }
}
