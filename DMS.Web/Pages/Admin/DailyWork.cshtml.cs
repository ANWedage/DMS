using System.ComponentModel.DataAnnotations;
using DMS.Models;
using DMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DMS.Web.Pages.Admin;

[Authorize(Roles = "Admin")]
public sealed class DailyWorkModel : PageModel
{
    private static readonly TimeSpan AdminAttendanceCutoff = new(17, 30, 0);
    private readonly DmsApiClient _apiClient;

    public DailyWorkModel(DmsApiClient apiClient) => _apiClient = apiClient;

    [BindProperty(SupportsGet = true)]
    [DataType(DataType.Date)]
    public DateTime SelectedDate { get; set; } = DateTime.Today;

    [BindProperty]
    public string? Action { get; set; }

    [BindProperty]
    public string? MeetingType { get; set; }

    [BindProperty]
    [StringLength(4000, MinimumLength = 3, ErrorMessage = "Use at least 3 characters for the daily task.")]
    public string Description { get; set; } = string.Empty;

    [BindProperty]
    public string Status { get; set; } = TaskStatuses.InProgress;

    [BindProperty]
    public string? BlockedReason { get; set; }

    public List<AdminAttendanceRecord> Attendance { get; private set; } = [];
    public AdminDailyTaskUpdate? DailyTask { get; private set; }
    public MeetingSettings MeetingSettings { get; private set; } = new();
    public bool IsWeekend => !MeetingSchedule.IsWorkingDay(SelectedDate);
    public bool IsFullDayLeave => Attendance.Count > 0
        && Attendance.All(record => record.Status == AttendanceStatuses.Leave);
    public bool HasSubmittedTask => DailyTask is not null;
    public bool CanEditAttendance => !IsWeekend
        && SelectedDate.Date == ApplicationNow.Date
        && ApplicationNow.TimeOfDay < AdminAttendanceCutoff;
    public string DisplayName => User.FindFirst("display_name")?.Value
        ?? User.Identity?.Name
        ?? "Administrator";

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        SelectedDate = SelectedDate.Date;
        if (!string.Equals(Action, "submit-task", StringComparison.Ordinal))
        {
            ModelState.Remove(nameof(Description));
            ModelState.Remove(nameof(Status));
            ModelState.Remove(nameof(BlockedReason));
        }

        try
        {
            var token = GetApiToken();
            switch (Action)
            {
                case "present":
                    await _apiClient.MarkAdminAttendancePresentAsync(
                        token, MeetingType ?? string.Empty, SelectedDate, cancellationToken);
                    TempData["SuccessMessage"] = $"{MeetingType} attendance marked present.";
                    break;

                case "leave":
                    await _apiClient.MarkAdminAttendanceLeaveAsync(
                        token, MeetingType ?? string.Empty, SelectedDate, cancellationToken);
                    TempData["SuccessMessage"] = $"{MeetingType} half-day leave marked.";
                    break;

                case "full-day-leave":
                    await _apiClient.MarkAdminFullDayLeaveAsync(token, SelectedDate, cancellationToken);
                    TempData["SuccessMessage"] = "Full-day leave marked.";
                    break;

                case "submit-task":
                    await SubmitTaskAsync(token, cancellationToken);
                    if (ModelState.IsValid)
                        TempData["SuccessMessage"] = "Daily task submitted successfully.";
                    break;

                default:
                    ModelState.AddModelError(string.Empty, "Choose a valid daily-work action.");
                    break;
            }

            if (ModelState.IsValid)
                return RedirectToPage(new { SelectedDate = SelectedDate.ToString("yyyy-MM-dd") });
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
        }

        await LoadAsync(cancellationToken, preserveTaskForm: string.Equals(Action, "submit-task", StringComparison.Ordinal));
        return Page();
    }

    private async Task SubmitTaskAsync(string token, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return;

        var attendanceTask = _apiClient.GetAdminAttendanceAsync(token, SelectedDate, cancellationToken);
        var dailyTaskTask = _apiClient.GetAdminDailyTaskAsync(token, SelectedDate, cancellationToken);
        await Task.WhenAll(attendanceTask, dailyTaskTask);

        var attendance = await attendanceTask;
        var existingTask = await dailyTaskTask;
        if (attendance.Count > 0 && attendance.All(record => record.Status == AttendanceStatuses.Leave))
            throw new InvalidOperationException(AdminDailyTaskStatuses.NotRequiredFullDayLeave);
        if (existingTask is not null)
            throw new InvalidOperationException("A daily task has already been submitted for this date.");
        if (Status == TaskStatuses.Blocked && string.IsNullOrWhiteSpace(BlockedReason))
        {
            ModelState.AddModelError(nameof(BlockedReason), "Enter the blocked reason.");
            return;
        }
        if (string.IsNullOrWhiteSpace(Description))
        {
            ModelState.AddModelError(nameof(Description), "Enter the work completed.");
            return;
        }
        if (Description.Trim().Length is < 3 or > 4000)
        {
            ModelState.AddModelError(nameof(Description), "Use between 3 and 4000 characters for the daily task.");
            return;
        }

        await _apiClient.SubmitAdminDailyTaskAsync(token, new AdminDailyTaskUpdate
        {
            UpdateDate = SelectedDate,
            Description = Description.Trim(),
            Status = Status,
            BlockedReason = string.IsNullOrWhiteSpace(BlockedReason) ? null : BlockedReason.Trim()
        }, cancellationToken);
    }

    private async Task LoadAsync(CancellationToken cancellationToken, bool preserveTaskForm = false)
    {
        var token = GetApiToken();
        MeetingSettings = await _apiClient.GetMeetingSettingsAsync(token, cancellationToken);
        MeetingSettings.EnsureTeamSettings();
        if (HttpContext.Request.Method == HttpMethods.Get
            && !HttpContext.Request.Query.ContainsKey(nameof(SelectedDate)))
            SelectedDate = ApplicationNow.Date;

        var attendanceTask = _apiClient.GetAdminAttendanceAsync(token, SelectedDate, cancellationToken);
        var dailyTaskTask = _apiClient.GetAdminDailyTaskAsync(token, SelectedDate, cancellationToken);
        await Task.WhenAll(attendanceTask, dailyTaskTask);

        Attendance = await attendanceTask;
        DailyTask = await dailyTaskTask;
        if (!preserveTaskForm)
        {
            Description = DailyTask?.Description ?? string.Empty;
            Status = DailyTask?.Status ?? TaskStatuses.InProgress;
            BlockedReason = DailyTask?.BlockedReason;
        }
    }

    private DateTime ApplicationNow => GetApplicationNow(MeetingSettings);

    private static DateTime GetApplicationNow(MeetingSettings settings)
    {
        var configuredTimeZone = settings.TimeZoneId?.Trim();
        if (string.IsNullOrWhiteSpace(configuredTimeZone)
            || string.Equals(configuredTimeZone, "Sri Lanka Standard Time", StringComparison.OrdinalIgnoreCase)
            || string.Equals(configuredTimeZone, "Asia/Colombo", StringComparison.OrdinalIgnoreCase))
            return DateTime.UtcNow.AddHours(5.5);

        try
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(configuredTimeZone);
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
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

    private string GetApiToken() =>
        User.FindFirst("api_token")?.Value
        ?? throw new InvalidOperationException("Your session has expired. Please sign in again.");
}
