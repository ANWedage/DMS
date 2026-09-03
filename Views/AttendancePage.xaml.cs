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

        private async Task LoadAttendanceAsync()
        {
            try
            {
                _settings = await Task.Run(_userService.GetMeetingSettings);
                var date = AttendanceDatePicker.SelectedDate ?? DateTime.Today;
                var records = await Task.Run(() => _userService.GetUserAttendance(_userId, date));
                var rows = new[]
                {
                    CreateRow(records, MeetingTypes.Morning, _settings.MorningTime, _settings.MorningMeetingLink),
                    CreateRow(records, MeetingTypes.Evening, _settings.EveningTime, _settings.EveningMeetingLink)
                };
                AttendanceItems.ItemsSource = rows;
                MessageText.Text = string.Empty;
            }
            catch (Exception ex)
            {
                AttendanceItems.ItemsSource = null;
                MessageText.Text = $"Unable to load attendance: {ex.Message}";
            }
        }

        private AttendanceRow CreateRow(List<AttendanceRecord> records, string meetingType, string meetingTime, string meetingLink)
        {
            var record = records.FirstOrDefault(item => item.MeetingType == meetingType)
                ?? new AttendanceRecord { MeetingType = meetingType, Status = AttendanceStatuses.Pending };
            var now = GetApplicationNow();
            var start = ParseMeetingStart(meetingTime, now.Date);
            return new AttendanceRow
            {
                RecordId = record.Id,
                MeetingType = meetingType,
                MeetingTime = meetingTime,
                MeetingLink = meetingLink,
                Status = record.Status,
                CanMarkPresent = record.Status == AttendanceStatuses.Pending
                    && now.Date == (AttendanceDatePicker.SelectedDate ?? DateTime.Today).Date
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
                $"Confirm that you attended the {meetingType} standup?",
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
            try
            {
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
                    TimeZoneInfo.FindSystemTimeZoneById(_settings.TimeZoneId));
            }
            catch
            {
                return DateTime.Now;
            }
        }

        private static DateTime ParseMeetingStart(string value, DateTime date)
        {
            return date.Add(TimeSpan.TryParseExact(value, @"hh\:mm", CultureInfo.InvariantCulture, out var time)
                ? time
                : new TimeSpan(0));
        }

        private sealed class AttendanceRow
        {
            public string RecordId { get; init; } = string.Empty;
            public string MeetingType { get; init; } = string.Empty;
            public string MeetingTime { get; init; } = string.Empty;
            public string MeetingLink { get; init; } = string.Empty;
            public string Status { get; set; } = AttendanceStatuses.Pending;
            public bool CanMarkPresent { get; set; }
        }
    }
}
