namespace DMS.Models
{
    public sealed record AdminAccountInfo(string Id, string Name, string Username);

    public sealed record AdminProfileUpdate(string CurrentUsername, string NewUsername,
        string CurrentPassword, string NewPassword);

    public sealed record UserProfileUpdate(string CurrentUsername, string NewUsername,
        string CurrentPassword, string NewPassword);
}