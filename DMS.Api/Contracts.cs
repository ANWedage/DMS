using DMS.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace DMS.Api;

public sealed record RegisterRequest(string Email, string ContactNumber, string Password, string Username);
public sealed record LoginRequest(string Username, string Password);
public sealed record SetUsernameRequest(string Username);
public sealed record AttendanceRequest(string MeetingType, DateOnly Date);
public sealed record UserStatusRequest(bool IsActive);
public sealed record AttendanceStatusRequest(string Status, string? Note);
public sealed record ComponentAssignmentsRequest(IReadOnlyCollection<string> UserIds);
public sealed record AuthResponse(string Token, string UserId, string? Username, string Role, string? DisplayName = null);

public sealed class JwtTokenService
{
    private readonly string _issuer;
    private readonly string _audience;
    private readonly SigningCredentials _credentials;

    public JwtTokenService(string issuer, string audience, SecurityKey signingKey)
    {
        _issuer = issuer;
        _audience = audience;
        _credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
    }

    public string CreateUserToken(User user) => CreateToken(user.Id, user.Username ?? user.Email, "User");

    public string CreateAdminToken(AdminUser admin) => CreateToken(admin.Id, admin.Username, "Admin", admin.Name);

    private string CreateToken(string subject, string username, string role, string? displayName = null)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subject),
            new(JwtRegisteredClaimNames.UniqueName, username),
            new(ClaimTypes.Role, role)
        };
        if (!string.IsNullOrWhiteSpace(displayName))
            claims.Add(new Claim("display_name", displayName));

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: _credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}