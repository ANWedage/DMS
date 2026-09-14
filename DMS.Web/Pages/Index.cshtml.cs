using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using DMS.Models;
using DMS.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DMS.Web.Pages;

[Authorize(Roles = "User")]
public sealed class IndexModel : PageModel
{
    private readonly DmsApiClient _apiClient;

    public IndexModel(DmsApiClient apiClient) => _apiClient = apiClient;

    public IReadOnlyList<AssignedTask> AssignedTasks { get; private set; } = [];
    public DailyTaskUpdate? TodayUpdate { get; private set; }
    public bool SubmittedToday => TodayUpdate is not null;
    public string DisplayName => User.Identity?.Name ?? "Developer";

    [BindProperty]
    public DailyUpdateForm Form { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        try
        {
            var token = GetApiToken();
            await LoadAsync(cancellationToken);
            if (SubmittedToday)
            {
                ModelState.AddModelError(string.Empty, "You have already submitted today's update.");
                return Page();
            }

            if (string.Equals(Form.UpdateType, DailyUpdateTypes.SelfStudy, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(Form.SelfStudyTopic))
            {
                ModelState.AddModelError(nameof(Form.SelfStudyTopic), "Enter the self-study topic.");
                return Page();
            }

            if (string.Equals(Form.UpdateType, DailyUpdateTypes.AssignedTask, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(Form.ComponentId))
            {
                ModelState.AddModelError(nameof(Form.ComponentId), "Choose an assigned task.");
                return Page();
            }

            if (Form.Status == TaskStatuses.Blocked && string.IsNullOrWhiteSpace(Form.BlockedReason))
            {
                ModelState.AddModelError(nameof(Form.BlockedReason), "Explain what is blocking this work.");
                return Page();
            }

            await _apiClient.SubmitAsync(token, new DailyTaskUpdate
            {
                UpdateType = Form.UpdateType,
                ComponentId = Form.ComponentId ?? string.Empty,
                UpdateDate = DateTime.SpecifyKind(DateTime.Today, DateTimeKind.Unspecified),
                Description = Form.Description.Trim(),
                SelfStudyTopic = string.IsNullOrWhiteSpace(Form.SelfStudyTopic) ? null : Form.SelfStudyTopic.Trim(),
                Status = Form.Status,
                BlockedReason = string.IsNullOrWhiteSpace(Form.BlockedReason) ? null : Form.BlockedReason.Trim()
            }, cancellationToken);

            TempData["SuccessMessage"] = "Your daily update was submitted successfully.";
            return RedirectToPage();
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var token = GetApiToken();
        var tasksTask = _apiClient.GetMyTasksAsync(token, cancellationToken);
        var historyTask = _apiClient.GetDailyHistoryAsync(token, cancellationToken);
        await Task.WhenAll(tasksTask, historyTask);
        AssignedTasks = await tasksTask;
        TodayUpdate = (await historyTask).FirstOrDefault(update => update.UpdateDate.Date == DateTime.Today);
    }

    private string GetApiToken() =>
        User.FindFirstValue("api_token")
        ?? throw new InvalidOperationException("Your session has expired. Please sign in again.");
}

public sealed class DailyUpdateForm
{
    [Required]
    public string UpdateType { get; set; } = DailyUpdateTypes.AssignedTask;

    public string? ComponentId { get; set; }

    public string? SelfStudyTopic { get; set; }

    [Required(ErrorMessage = "Describe the work you completed.")]
    [StringLength(4000, MinimumLength = 3, ErrorMessage = "Use at least 3 characters for the daily update.")]
    public string Description { get; set; } = string.Empty;

    [Required]
    public string Status { get; set; } = TaskStatuses.InProgress;

    public string? BlockedReason { get; set; }
}
