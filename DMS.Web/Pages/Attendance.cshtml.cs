using System.ComponentModel.DataAnnotations;
using DMS.Models;
using DMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DMS.Web.Pages;

[Authorize(Roles = "User")]
public sealed class AttendanceModel : PageModel
{
    private readonly DmsApiClient _apiClient;

    public AttendanceModel(DmsApiClient apiClient) => _apiClient = apiClient;

    [BindProperty(SupportsGet = true)]
    [DataType(DataType.Date)]
    public DateTime SelectedDate { get; set; } = DateTime.Today;

    [BindProperty]
    public string? MeetingType { get; set; }

    public bool IsWeekend => !MeetingSchedule.IsWorkingDay(SelectedDate);
    public List<AttendanceSlotViewModel> Slots { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(MeetingType))
        {
            try
            {
                var token = GetApiToken();
                await _apiClient.MarkAttendancePresentAsync(token, MeetingType, SelectedDate, cancellationToken);
                TempData["SuccessMessage"] = $"Attendance recorded for {MeetingType}.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToPage(new { SelectedDate = SelectedDate.ToString("yyyy-MM-dd") });
        }

        await LoadAsync(cancellationToken);
        return Page();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (!MeetingSchedule.IsWorkingDay(SelectedDate))
        {
            Slots = [];
            return;
        }

        var token = GetApiToken();
        var settings = await _apiClient.GetMeetingSettingsAsync(token, cancellationToken);
        var attendance = await _apiClient.GetUserAttendanceAsync(token, SelectedDate, cancellationToken);

        Slots = MeetingSchedule.ForDate(settings, SelectedDate)
            .Select(slot =>
            {
                var record = attendance.FirstOrDefault(item => item.MeetingType == slot.Type);
                var status = record?.Status ?? AttendanceStatuses.Pending;
                var now = GetApplicationNow(settings);
                var start = GetMeetingStart(slot.Type, settings, SelectedDate.Date);
                var canMarkPresent = record is null
                    && status == AttendanceStatuses.Pending
                    && now.Date == SelectedDate.Date
                    && now >= start
                    && now <= start.AddMinutes(15);

                return new AttendanceSlotViewModel
                {
                    MeetingType = slot.Type,
                    DisplayName = slot.DisplayName,
                    Time = slot.Time,
                    Link = slot.Link,
                    Status = status,
                    CanMarkPresent = canMarkPresent,
                    StatusCssClass = status switch
                    {
                        AttendanceStatuses.Present => "success",
                        AttendanceStatuses.Absent => "danger",
                        AttendanceStatuses.AbsentInformed => "warning",
                        _ => "neutral"
                    }
                };
            })
            .ToList();
    }

    private static DateTime GetApplicationNow(MeetingSettings settings)
    {
        try
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
        }
        catch
        {
            return DateTime.Now;
        }
    }

    private static DateTime GetMeetingStart(string meetingType, MeetingSettings settings, DateTime date)
    {
        var timeValue = meetingType switch
        {
            MeetingTypes.Morning => settings.MorningTime,
            MeetingTypes.Weekly => settings.WeeklyTime,
            MeetingTypes.Evening => settings.EveningTime,
            _ => "00:00"
        };

        return date.Add(TimeSpan.TryParse(timeValue, out var time)
            ? time
            : TimeSpan.Zero);
    }

    private string GetApiToken() =>
        User.FindFirst("api_token")?.Value
        ?? throw new InvalidOperationException("Your session has expired. Please sign in again.");

    public sealed class AttendanceSlotViewModel
    {
        public string MeetingType { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Time { get; set; } = string.Empty;
        public string Link { get; set; } = string.Empty;
        public string Status { get; set; } = AttendanceStatuses.Pending;
        public bool CanMarkPresent { get; set; }
        public string StatusCssClass { get; set; } = "neutral";
    }
}
