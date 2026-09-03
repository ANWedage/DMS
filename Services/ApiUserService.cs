using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Http;
using System.Text.Json;
using DMS.Data;
using DMS.Helpers;
using DMS.Models;

namespace DMS.Services;

public sealed class ApiUserService : IUserService, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public ApiUserService(string? baseUrl = null)
    {
        var configuredUrl = baseUrl
            ?? MongoConfig.GetEnvironmentValue("DMS_API_BASE_URL")
            ?? "http://127.0.0.1:5188";
        _httpClient = new HttpClient { BaseAddress = new Uri(configuredUrl.TrimEnd('/') + "/") };
    }

    public User CreateAccount(string email, string contactNumber, string password, string username)
    {
        var response = Send(HttpMethod.Post, "api/auth/register", new { email, contactNumber, password, username });
        var auth = Read<AuthResponse>(response);
        AppSession.SetAccessToken(auth.Token);
        var user = new User { Id = auth.UserId, Email = email, ContactNumber = contactNumber, Username = auth.Username };
        AppSession.SetCurrentUser(user);
        return user;
    }

    public void SetUsername(string userId, string username)
    {
        EnsureCurrentUser(userId);
        Send(HttpMethod.Post, "api/users/me/username", new { username }).Dispose();
    }

    public User? GetUserByUsername(string username) => null;

    public bool SetUserStatus(string userId, bool isActive, string? adminName = null)
    {
        using var response = Send(HttpMethod.Post, $"api/admin/users/{Uri.EscapeDataString(userId)}/status", new { isActive });
        return response.IsSuccessStatusCode;
    }

    public bool DeleteUserAccount(string userId)
    {
        using var response = Send(HttpMethod.Post, $"api/admin/users/{Uri.EscapeDataString(userId)}/delete", allowErrorResponse: true);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            response.Dispose();
            using var deleteResponse = Send(HttpMethod.Delete, $"api/admin/users/{Uri.EscapeDataString(userId)}", allowErrorResponse: true);
            EnsureDeleteSucceeded(deleteResponse);
            return true;
        }

        EnsureDeleteSucceeded(response);
        return true;
    }

    private static void EnsureDeleteSucceeded(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            var message = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(message)
                    ? $"The API returned {(int)response.StatusCode} ({response.StatusCode})."
                    : message);
        }
    }

    public User? Login(string username, string password)
    {
        using var response = Send(HttpMethod.Post, "api/auth/login", new { username, password }, allowErrorResponse: true);
        if (!response.IsSuccessStatusCode)
            return null;

        var auth = Read<AuthResponse>(response);
        if (!string.Equals(auth.Role, "User", StringComparison.OrdinalIgnoreCase))
            return null;

        AppSession.SetAccessToken(auth.Token);
        return Read<User>(Send(HttpMethod.Get, "api/users/me"));
    }

    public AdminUser? LoginAdmin(string username, string password)
    {
        using var response = Send(HttpMethod.Post, "api/auth/login", new { username, password }, allowErrorResponse: true);
        if (!response.IsSuccessStatusCode)
            return null;

        var auth = Read<AuthResponse>(response);
        if (!string.Equals(auth.Role, "Admin", StringComparison.OrdinalIgnoreCase))
            return null;

        AppSession.SetAccessToken(auth.Token);
        return new AdminUser { Id = auth.UserId, Username = auth.Username ?? username, Name = auth.DisplayName ?? username };
    }

    public User GetUserById(string userId)
    {
        EnsureCurrentUser(userId);
        using var response = Send(HttpMethod.Get, "api/users/me");
        return Read<User>(response);
    }

    public List<User> GetAllUsers() => Read<List<User>>(Send(HttpMethod.Get, "api/admin/users"));

    public long GetActiveUserCount() => Read<long>(Send(HttpMethod.Get, "api/admin/users/active-count"));

    public MeetingSettings GetMeetingSettings() => Read<MeetingSettings>(Send(HttpMethod.Get, "api/meeting-settings"));

    public void SaveMeetingSettings(MeetingSettings settings, string adminId, string adminName)
    {
        using var response = Send(HttpMethod.Put, "api/admin/meeting-settings", settings);
    }

    public List<AttendanceRecord> GetUserAttendance(string userId, DateTime date)
    {
        EnsureCurrentUser(userId);
        return Read<List<AttendanceRecord>>(Send(HttpMethod.Get, $"api/attendance?date={date:yyyy-MM-dd}"));
    }

    public List<AttendanceRecord> GetAllAttendance(DateTime date) =>
        Read<List<AttendanceRecord>>(Send(HttpMethod.Get, $"api/admin/attendance?date={date:yyyy-MM-dd}"));

    public bool MarkAttendancePresent(string userId, string meetingType, DateTime date)
    {
        EnsureCurrentUser(userId);
        using var response = Send(HttpMethod.Post, "api/attendance/present", new { meetingType, date }, allowErrorResponse: true);
        return response.IsSuccessStatusCode;
    }

    public bool UpdateAttendanceStatus(string attendanceId, string status, string adminId, string adminName, string? note)
    {
        using var response = Send(HttpMethod.Post, $"api/admin/attendance/{Uri.EscapeDataString(attendanceId)}/status", new { status, note });
        return response.IsSuccessStatusCode;
    }

    public bool CanAccessUser(string targetUserId) =>
        !AppSession.IsAdmin && string.Equals(AppSession.CurrentUserId, targetUserId, StringComparison.Ordinal);

    public bool EmailExists(string email) => throw new NotSupportedException("Email checks are performed by the API during registration.");

    public bool UsernameExists(string username)
    {
        var encodedUsername = Uri.EscapeDataString(SecurityValidator.NormalizeUsername(username));
        return Read<bool>(Send(HttpMethod.Get, $"api/auth/username-exists?username={encodedUsername}"));
    }

    private HttpResponseMessage Send(HttpMethod method, string path, object? body = null, bool allowErrorResponse = false)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body != null)
            request.Content = JsonContent.Create(body, options: _jsonOptions);
        if (!string.IsNullOrWhiteSpace(AppSession.AccessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AppSession.AccessToken);

        var response = _httpClient.Send(request);
        if (!allowErrorResponse && !response.IsSuccessStatusCode)
        {
            var message = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            response.Dispose();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? "The API request failed." : message);
        }

        return response;
    }

    private T Read<T>(HttpResponseMessage response)
    {
        using (response)
            return response.Content.ReadFromJsonAsync<T>(_jsonOptions).GetAwaiter().GetResult()
                ?? throw new InvalidOperationException("The API returned an empty response.");
    }

    private static void EnsureCurrentUser(string userId)
    {
        if (!string.Equals(AppSession.CurrentUserId, userId, StringComparison.Ordinal))
            throw new InvalidOperationException("You do not have access to this user account.");
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed record AuthResponse(string Token, string UserId, string? Username, string Role, string? DisplayName);
}