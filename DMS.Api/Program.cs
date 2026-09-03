using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DMS.Api;
using DMS.Data;
using DMS.Models;
using DMS.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var mongoConnectionString = MongoConfig.GetConnectionString(throwIfMissing: true);
var jwtSecret = builder.Configuration["Jwt:Secret"];
if (string.IsNullOrWhiteSpace(jwtSecret))
    jwtSecret = Environment.GetEnvironmentVariable("DMS_JWT_SECRET");
if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
    throw new InvalidOperationException("Jwt:Secret or DMS_JWT_SECRET must contain at least 32 characters.");

var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "DMS.Api";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "DMS.Desktop";
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

builder.Services.AddSingleton(new MongoDbContext());
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddSingleton(new JwtTokenService(jwtIssuer, jwtAudience, signingKey));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/auth/username-exists", (string username, IUserService users) =>
    Results.Ok(users.UsernameExists(username)));

app.MapPost("/api/auth/register", (RegisterRequest request, IUserService users, JwtTokenService tokens) =>
{
    try
    {
        var user = users.CreateAccount(request.Email, request.ContactNumber, request.Password, request.Username);
        return Results.Ok(new AuthResponse(tokens.CreateUserToken(user), user.Id, user.Username, "User"));
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/auth/login", (LoginRequest request, IUserService users, JwtTokenService tokens) =>
{
    try
    {
        var user = users.Login(request.Username, request.Password);
        if (user != null)
            return Results.Ok(new AuthResponse(tokens.CreateUserToken(user), user.Id, user.Username, "User"));
    }
    catch (AccountDisabledException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
    }

    var admin = users.LoginAdmin(request.Username, request.Password);
    if (admin != null)
        return Results.Ok(new AuthResponse(tokens.CreateAdminToken(admin), admin.Id, admin.Username, "Admin", admin.Name));

    return Results.Unauthorized();
});

var authenticated = app.MapGroup("/api").RequireAuthorization();

authenticated.MapGet("/users/me", (ClaimsPrincipal principal, IUserService users) =>
{
    var userId = GetSubject(principal);
    if (principal.IsInRole("Admin") || string.IsNullOrWhiteSpace(userId))
        return Results.Forbid();

    return Results.Ok(users.GetUserById(userId));
});

authenticated.MapPost("/users/me/username", (ClaimsPrincipal principal, SetUsernameRequest request, IUserService users) =>
{
    var userId = GetSubject(principal);
    if (principal.IsInRole("Admin") || string.IsNullOrWhiteSpace(userId))
        return Results.Forbid();

    try
    {
        users.SetUsername(userId, request.Username);
        return Results.NoContent();
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

authenticated.MapGet("/attendance", (ClaimsPrincipal principal, DateTime? date, IUserService users) =>
{
    var userId = GetSubject(principal);
    if (principal.IsInRole("Admin") || string.IsNullOrWhiteSpace(userId))
        return Results.Forbid();

    return Results.Ok(users.GetUserAttendance(userId, date?.Date ?? DateTime.Today));
});

authenticated.MapPost("/attendance/present", (ClaimsPrincipal principal, AttendanceRequest request, IUserService users) =>
{
    var userId = GetSubject(principal);
    if (principal.IsInRole("Admin") || string.IsNullOrWhiteSpace(userId))
        return Results.Forbid();

    try
    {
        users.MarkAttendancePresent(userId, request.MeetingType, request.Date.ToDateTime(TimeOnly.MinValue));
        return Results.NoContent();
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

authenticated.MapGet("/admin/users", (ClaimsPrincipal principal, IUserService users) =>
    principal.IsInRole("Admin") ? Results.Ok(users.GetAllUsers()) : Results.Forbid());

authenticated.MapGet("/admin/users/active-count", (ClaimsPrincipal principal, IUserService users) =>
    principal.IsInRole("Admin") ? Results.Ok(users.GetActiveUserCount()) : Results.Forbid());

authenticated.MapPost("/admin/users/{userId}/status", (string userId, UserStatusRequest request, ClaimsPrincipal principal, IUserService users) =>
{
    if (!principal.IsInRole("Admin"))
        return Results.Forbid();

    var adminName = principal.FindFirst("display_name")?.Value;
    return users.SetUserStatus(userId, request.IsActive, adminName)
        ? Results.NoContent()
        : Results.NotFound();
});

authenticated.MapDelete("/admin/users/{userId}", (string userId, ClaimsPrincipal principal, IUserService users) =>
{
    if (!principal.IsInRole("Admin"))
        return Results.Forbid();

    return users.DeleteUserAccount(userId)
        ? Results.NoContent()
        : Results.NotFound();
});

authenticated.MapPost("/admin/users/{userId}/delete", (string userId, ClaimsPrincipal principal, IUserService users) =>
{
    if (!principal.IsInRole("Admin"))
        return Results.Forbid();

    return users.DeleteUserAccount(userId)
        ? Results.NoContent()
        : Results.NotFound();
});

authenticated.MapGet("/admin/attendance", (DateTime? date, ClaimsPrincipal principal, IUserService users) =>
    principal.IsInRole("Admin")
        ? Results.Ok(users.GetAllAttendance(date?.Date ?? DateTime.Today))
        : Results.Forbid());

authenticated.MapPost("/admin/attendance/{attendanceId}/status", (string attendanceId, AttendanceStatusRequest request, ClaimsPrincipal principal, IUserService users) =>
{
    if (!principal.IsInRole("Admin"))
        return Results.Forbid();

    var adminId = GetSubject(principal);
    var adminName = principal.FindFirst("display_name")?.Value ?? principal.Identity?.Name ?? "Admin";
    return users.UpdateAttendanceStatus(attendanceId, request.Status, adminId ?? string.Empty, adminName, request.Note)
        ? Results.NoContent()
        : Results.BadRequest(new { error = "Attendance status could not be updated." });
});

authenticated.MapGet("/meeting-settings", (ClaimsPrincipal principal, IUserService users) =>
    Results.Ok(users.GetMeetingSettings()));

authenticated.MapPut("/admin/meeting-settings", (MeetingSettings settings, ClaimsPrincipal principal, IUserService users) =>
{
    if (!principal.IsInRole("Admin"))
        return Results.Forbid();

    try
    {
        var adminId = GetSubject(principal) ?? string.Empty;
        var adminName = principal.FindFirst("display_name")?.Value ?? "Admin";
        users.SaveMeetingSettings(settings, adminId, adminName);
        return Results.NoContent();
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.Run();

static string? GetSubject(ClaimsPrincipal principal)
{
    return principal.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
}

public partial class Program;