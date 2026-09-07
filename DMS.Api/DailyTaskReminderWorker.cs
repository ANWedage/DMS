using DMS.Models;
using DMS.Services;

namespace DMS.Api;

public sealed class DailyTaskReminderWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private DateOnly? _lastReminderDate;

    public DailyTaskReminderWorker(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var users = scope.ServiceProvider.GetRequiredService<IUserService>();
                var settings = users.GetMeetingSettings();
                var now = GetApplicationNow(settings);
                if (now.Hour == 16 && now.Minute >= 50 && now.Minute < 51 && _lastReminderDate != DateOnly.FromDateTime(now))
                {
                    users.EnsureDailyTaskReminder(now.Date);
                    _lastReminderDate = DateOnly.FromDateTime(now);
                }
            }
            catch
            {
                // A later tick retries without stopping the API.
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private static DateTime GetApplicationNow(MeetingSettings settings)
    {
        var configuredTimeZone = settings.TimeZoneId?.Trim();
        if (string.IsNullOrWhiteSpace(configuredTimeZone)
            || string.Equals(configuredTimeZone, "Sri Lanka Standard Time", StringComparison.OrdinalIgnoreCase)
            || string.Equals(configuredTimeZone, "Asia/Colombo", StringComparison.OrdinalIgnoreCase))
            return DateTime.UtcNow.AddHours(5.5);

        try
        {
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
                TimeZoneInfo.FindSystemTimeZoneById(configuredTimeZone));
        }
        catch
        {
            return DateTime.UtcNow.AddHours(5.5);
        }
    }
}