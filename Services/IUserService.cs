using DMS.Models;

namespace DMS.Services
{
    public interface IUserService
    {
        /// <summary>Creates an account with email, contact number, and password. Username is not set yet.</summary>
        /// <exception cref="InvalidOperationException">Thrown if the email is already registered.</exception>
        User CreateAccount(string email, string contactNumber, string password, string username);

        /// <summary>Sets the username for a freshly created account (post-signup popup step).</summary>
        /// <exception cref="InvalidOperationException">Thrown if the username is already taken.</exception>
        void SetUsername(string userId, string username);

        /// <summary>Looks up a user by username for validation and status checks.</summary>
        User? GetUserByUsername(string username);

        /// <summary>Updates the active/deactivated state of a user account.</summary>
        bool SetUserStatus(string userId, bool isActive, string? adminName = null);

        /// <summary>Deletes a user account and its attendance records.</summary>
        bool DeleteUserAccount(string userId);

        /// <summary>Validates username + password. Returns the user on success, null otherwise.</summary>
        User? Login(string username, string password);

        /// <summary>Returns the configured admin account matching the supplied username, or null.</summary>
        AdminUser? LoginAdmin(string username, string password);

        /// <summary>Loads a user profile by ID so a session can only ever show its own account.</summary>
        User GetUserById(string userId);

        /// <summary>Returns all registered users so administrators can review the developer list.</summary>
        List<User> GetAllUsers();

        /// <summary>Returns the number of currently active user accounts.</summary>
        long GetActiveUserCount();

        MeetingSettings GetMeetingSettings();
        void SaveMeetingSettings(MeetingSettings settings, string adminId, string adminName);
        List<AttendanceRecord> GetUserAttendance(string userId, DateTime date);
        List<AttendanceRecord> GetAllAttendance(DateTime date);
        bool MarkAttendancePresent(string userId, string meetingType, DateTime date);
        bool UpdateAttendanceStatus(string attendanceId, string status, string adminId, string adminName, string? note);

        /// <summary>Returns true only when the active session belongs to the target user.</summary>
        bool CanAccessUser(string targetUserId);

        bool EmailExists(string email);
        bool UsernameExists(string username);
    }
}
