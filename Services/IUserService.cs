using DMS.Models;

namespace DMS.Services
{
    public sealed class AccountDisabledException : InvalidOperationException
    {
        public AccountDisabledException()
            : base("Your account has been disabled by an administrator. Please contact an administrator for access.")
        {
        }
    }

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

        /// <summary>Returns true when the supplied username exists for the selected user/admin role.</summary>
        bool UsernameExistsForRole(string username, bool isAdmin);

        /// <summary>Resets the password for the matching user/admin account using the supplied username.</summary>
        bool ResetPassword(string username, string newPassword, bool isAdmin);

        /// <summary>Updates the active/deactivated state of a user account.</summary>
        bool SetUserStatus(string userId, bool isActive, string? adminName = null);

        /// <summary>Updates the developer role/position for a user account.</summary>
        bool SetUserPosition(string userId, string? position);

        /// <summary>Updates the date an internship ends for a user account.</summary>
        bool SetUserLeavingDate(string userId, DateTime? leavingDate);

        /// <summary>Deletes a user account and its attendance records.</summary>
        bool DeleteUserAccount(string userId);

        /// <summary>Validates username + password. Returns the user on success, null otherwise.</summary>
        User? Login(string username, string password);
        UserAccountInfo UpdateUserProfile(string userId, UserProfileUpdate update);

        /// <summary>Returns the configured admin account matching the supplied username, or null.</summary>
        AdminUser? LoginAdmin(string username, string password);
        AdminAccountInfo UpdateAdminProfile(string adminId, AdminProfileUpdate update);

        /// <summary>Loads a user profile by ID so a session can only ever show its own account.</summary>
        User GetUserById(string userId);

        /// <summary>Returns all registered users so administrators can review the developer list.</summary>
        List<User> GetAllUsers();
        List<DeveloperDailyTaskStatus> GetDeveloperDailyTaskStatus(DateTime date);
        List<AdminAccountInfo> GetAllAdmins();

        /// <summary>Returns the number of currently active user accounts.</summary>
        long GetActiveUserCount();
        long DeleteOldDeveloperData(DateTime keepFromDate);

        List<Notification> GetNotifications(string recipientId, string recipientRole);
        List<Notification> GetSentNotifications(string senderId, string senderRole);
        long GetUnreadNotificationCount(string recipientId, string recipientRole);
        bool MarkNotificationRead(string notificationId, string recipientId, string recipientRole);
        bool MarkAllNotificationsRead(string recipientId, string recipientRole);
        void EnsureDailyTaskReminder(DateTime localDate);
        int SendNotification(string senderId, string senderName, string recipientRole, bool sendToAll,
            IReadOnlyCollection<string> recipientIds, string title, string message);

        MeetingSettings GetMeetingSettings();
        MeetingSettings GetMeetingSettingsForUser(string userId);
        void SaveMeetingSettings(MeetingSettings settings, string adminId, string adminName);
        List<AttendanceRecord> GetUserAttendance(string userId, DateTime date);
        bool IsUserFullDayLeave(string userId, DateTime date);
        List<AttendanceRecord> GetAllAttendance(DateTime date);
        bool MarkAttendancePresent(string userId, string meetingType, DateTime date);
        bool UpdateAttendanceStatus(string attendanceId, string status, string adminId, string adminName, string? note);
        List<AdminAttendanceRecord> GetAdminAttendance(string adminId, DateTime date);
        bool MarkAdminAttendancePresent(string adminId, string meetingType, DateTime date);
        bool MarkAdminAttendanceLeave(string adminId, string meetingType, DateTime date);
        bool MarkAdminAttendanceFullDayLeave(string adminId, DateTime date);
        void EnsureAdminAttendance(DateTime date);
        List<AdminAttendanceReportRow> GetAllAdminAttendance(DateTime date);

        /// <summary>Returns true only when the active session belongs to the target user.</summary>
        bool CanAccessUser(string targetUserId);

        List<ChatUser> GetChatUsers(string currentUserId, string currentRole);
        List<ChatConversationSummary> GetChatInbox(string currentUserId, string currentRole);
        List<ChatConversationSummary> GetChatSent(string currentUserId, string currentRole);
        long GetUnreadChatCount(string currentUserId, string currentRole);
        List<ChatMessage> GetChatMessages(string currentUserId, string currentRole, string otherUserId, string otherRole);
        bool DeleteChatConversation(string currentUserId, string currentRole, string otherUserId, string otherRole);
        ChatAttachment? GetChatAttachment(string currentUserId, string currentRole, string attachmentId);
        ChatAttachmentDownloadResponse DownloadChatAttachment(string currentUserId, string currentRole, string attachmentId);
        ChatAttachment UploadChatAttachment(string senderId, string senderRole, string recipientId, string recipientRole, string fileName, byte[] content);
        bool DeleteChatAttachment(string currentUserId, string currentRole, string attachmentId);
        ChatMessage SaveChatMessage(string senderId, string senderRole, string recipientId, string recipientRole, string messageText, string? attachmentId = null);
        bool MarkChatMessagesRead(string recipientId, string recipientRole, string senderId, string senderRole);

        bool EmailExists(string email);
        bool UsernameExists(string username);

        List<TaskProject> GetProjects();
        TaskProject CreateProject(TaskProject project);
        TaskProject UpdateProject(TaskProject project);
        bool DeleteProject(string projectId);
        List<TaskComponent> GetProjectComponents(string projectId);
        TaskComponent CreateTaskComponent(TaskComponent component);
        TaskComponent UpdateTaskComponent(TaskComponent component);
        bool DeleteTaskComponent(string componentId);
        List<ComponentAssignment> GetComponentAssignments(string componentId);
        bool SetComponentAssignments(string componentId, IReadOnlyCollection<string> userIds, string adminId);
        List<AssignedTask> GetMyTasks(string userId);
        List<DailyTaskUpdate> GetTaskUpdates(string componentId, string userId, bool isAdmin);
        List<DailyTaskUpdate> GetMyDailyHistory(string userId);
        List<DailyTaskUpdate> GetSelfStudyUpdates(string userId, bool isAdmin);
        DailyTaskUpdate SaveDailyTaskUpdate(DailyTaskUpdate update);
        List<ProjectDailyTaskReportRow> GetProjectDailyTaskReport(string projectId, DateTime date);
        AdminDailyTaskUpdate? GetAdminDailyTask(string adminId, DateTime date);
        AdminDailyTaskUpdate SaveAdminDailyTask(AdminDailyTaskUpdate update);
        List<AdminDailyTaskReportRow> GetAllAdminDailyTasks(DateTime date);
    }
}
