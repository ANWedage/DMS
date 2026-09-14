using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DMS.Models;

namespace DMS.Web.Services;

public sealed class DmsApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public DmsApiClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _httpClient.BaseAddress = new Uri(GetBaseUrl());
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
    }

    public async Task<ApiLoginResult> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("api/auth/login", new { username, password }, _jsonOptions, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new InvalidOperationException("This account is disabled. Please contact an administrator.");
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("The username or password is incorrect.");

        await EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<ApiLoginResult>(_jsonOptions, cancellationToken);
        return result ?? throw new InvalidOperationException("The API returned an empty login response.");
    }

    public Task<List<AssignedTask>> GetMyTasksAsync(string token, CancellationToken cancellationToken = default) =>
        GetAsync<List<AssignedTask>>("api/tasks/my", token, cancellationToken);

    public Task<List<DailyTaskUpdate>> GetDailyHistoryAsync(string token, CancellationToken cancellationToken = default) =>
        GetAsync<List<DailyTaskUpdate>>("api/tasks/my/daily-history", token, cancellationToken);

    public async Task SubmitAsync(string token, DailyTaskUpdate update, CancellationToken cancellationToken = default)
    {
        var path = string.Equals(update.UpdateType, DailyUpdateTypes.SelfStudy, StringComparison.OrdinalIgnoreCase)
            ? "api/self-study/updates"
            : $"api/tasks/{Uri.EscapeDataString(update.ComponentId)}/updates";
        using var request = CreateRequest(HttpMethod.Post, path, token);
        request.Content = JsonContent.Create(update, options: _jsonOptions);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private async Task<T> GetAsync<T>(string path, string token, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, path, token);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The API returned an empty response.");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
                throw new InvalidOperationException(error.GetString() ?? "The API request failed.");
        }
        catch (JsonException)
        {
            // Fall through to the generic response below.
        }

        throw new InvalidOperationException(string.IsNullOrWhiteSpace(body)
            ? $"The API request failed ({(int)response.StatusCode})."
            : body);
    }

    private string GetBaseUrl()
    {
        var configuredUrl = Environment.GetEnvironmentVariable("DMS_API_BASE_URL")
            ?? _configuration["DmsApi:BaseUrl"]
            ?? throw new InvalidOperationException("DMS_API_BASE_URL is not configured.");
        return configuredUrl.TrimEnd('/') + "/";
    }
}

public sealed record ApiLoginResult(string Token, string UserId, string? Username, string Role, string? DisplayName);
