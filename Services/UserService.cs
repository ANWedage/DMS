using MongoDB.Driver;
using System.Globalization;
using DMS.Data;
using DMS.Helpers;
using DMS.Models;

namespace DMS.Services
{
    public class UserService : IUserService
    {
        private readonly MongoDbContext _context;

        public UserService(MongoDbContext context)
        {
            _context = context;
        }

        public User CreateAccount(string email, string contactNumber, string password, string username)
        {
            var trimmedEmail = SecurityValidator.NormalizeEmail(email);
            var trimmedContactNumber = contactNumber.Trim();
            var trimmedPassword = password ?? string.Empty;
            var normalizedUsername = SecurityValidator.NormalizeUsername(username);

            if (!SecurityValidator.IsValidEmail(trimmedEmail))
                throw new InvalidOperationException("Enter a valid email address.");

            if (!SecurityValidator.IsStrongPassword(trimmedPassword))
                throw new InvalidOperationException("Password must be at least 8 characters and include uppercase, lowercase, a number, and a symbol.");

            if (!SecurityValidator.IsValidUsername(normalizedUsername))
                throw new InvalidOperationException("Username must be 3-20 characters, letters/numbers/underscore only, and contain no spaces.");

            if (EmailExists(trimmedEmail))
                throw new InvalidOperationException("An account with this email already exists.");

            if (UsernameExists(normalizedUsername))
                throw new InvalidOperationException("That username is already taken.");

            var (hash, salt) = PasswordHasher.HashPassword(trimmedPassword);

            var user = new User
            {
                Email = trimmedEmail,
                ContactNumber = trimmedContactNumber,
                PasswordHash = hash,
                PasswordSalt = salt,
                Username = normalizedUsername
            };

            _context.Users.InsertOne(user);
            return user;
        }

        public void SetUsername(string userId, string username)
        {
            var normalizedUsername = SecurityValidator.NormalizeUsername(username);
            if (!SecurityValidator.IsValidUsername(normalizedUsername))
                throw new InvalidOperationException("Username must be 3-20 characters, letters/numbers/underscore only, and contain no spaces.");

            if (UsernameExists(normalizedUsername))
                throw new InvalidOperationException("That username is already taken.");

            var filter = Builders<User>.Filter.Eq(u => u.Id, userId);
            var update = Builders<User>.Update.Set(u => u.Username, normalizedUsername);
            _context.Users.UpdateOne(filter, update);
        }

        public User? GetUserByUsername(string username)
        {
            var normalizedUsername = SecurityValidator.NormalizeUsername(username);
            return _context.Users.Find(u => u.Username == normalizedUsername).FirstOrDefault();
        }

        public bool SetUserStatus(string userId, bool isActive, string? adminName = null)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return false;

            var filter = Builders<User>.Filter.Eq(u => u.Id, userId);
            var update = Builders<User>.Update
                .Set(u => u.IsActive, isActive)
                .Set(u => u.DeactivatedByAdminName, isActive ? null : adminName);
            var result = _context.Users.UpdateOne(filter, update);
            return result.ModifiedCount > 0;
        }

        public bool DeleteUserAccount(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return false;

            _context.Attendance.DeleteMany(a => a.UserId == userId);
            return _context.Users.DeleteOne(u => u.Id == userId).DeletedCount > 0;
        }

        public User? Login(string username, string password)
        {
            var normalizedUsername = SecurityValidator.NormalizeUsername(username);
            var user = _context.Users.Find(u => u.Username == normalizedUsername).FirstOrDefault();
            if (user is null) return null;
            if (!user.IsActive)
                throw new AccountDisabledException();

            return PasswordHasher.Verify(password ?? string.Empty, user.PasswordHash, user.PasswordSalt)
                ? user
                : null;
        }

        public AdminUser? LoginAdmin(string username, string password)
        {
            var normalizedUsername = SecurityValidator.NormalizeUsername(username);
            var admin = _context.Admins.Find(a => a.Username == normalizedUsername).FirstOrDefault();
            if (admin is null) return null;

            return PasswordHasher.Verify(password ?? string.Empty, admin.PasswordHash, admin.PasswordSalt)
                ? admin
                : null;
        }

        public AdminAccountInfo UpdateAdminUsername(string adminId, string currentPassword, string newUsername)
        {
            var admin = GetAdminForUpdate(adminId, currentPassword);
            var normalizedUsername = SecurityValidator.NormalizeUsername(newUsername);
            if (!SecurityValidator.IsValidUsername(normalizedUsername))
                throw new InvalidOperationException("Username must be 3-20 characters, letters/numbers/underscore only, and contain no spaces.");
            if (_context.Admins.Find(a => a.Username == normalizedUsername && a.Id != adminId).Any())
                throw new InvalidOperationException("That username is already taken.");

            _context.Admins.UpdateOne(a => a.Id == adminId,
                Builders<AdminUser>.Update.Set(a => a.Username, normalizedUsername));
            admin.Username = normalizedUsername;
            return new AdminAccountInfo(admin.Id, admin.Name, admin.Username);
        }

        public void UpdateAdminPassword(string adminId, string currentPassword, string newPassword)
        {
            GetAdminForUpdate(adminId, currentPassword);
            if (!SecurityValidator.IsStrongPassword(newPassword))
                throw new InvalidOperationException("Password must be at least 8 characters and include uppercase, lowercase, a number, and a symbol.");

            var (hash, salt) = PasswordHasher.HashPassword(newPassword);
            _context.Admins.UpdateOne(a => a.Id == adminId,
                Builders<AdminUser>.Update.Set(a => a.PasswordHash, hash).Set(a => a.PasswordSalt, salt));
        }

        private AdminUser GetAdminForUpdate(string adminId, string currentPassword)
        {
            var admin = _context.Admins.Find(a => a.Id == adminId).FirstOrDefault();
            if (admin == null || !PasswordHasher.Verify(currentPassword ?? string.Empty, admin.PasswordHash, admin.PasswordSalt))
                throw new InvalidOperationException("The current password is incorrect.");
            return admin;
        }

        public User GetUserById(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new InvalidOperationException("User session is missing.");

            var user = _context.Users.Find(u => u.Id == userId).FirstOrDefault();
            if (user == null)
                throw new InvalidOperationException("This user account could not be found.");

            return user;
        }

        public List<User> GetAllUsers()
        {
            var users = _context.Users.Find(_ => true).ToList();
            return users
                .OrderBy(u => string.IsNullOrWhiteSpace(u.Username) ? u.Email : u.Username)
                .ToList();
        }

        public long GetActiveUserCount()
        {
            return _context.Users.CountDocuments(u => u.IsActive);
        }

        public List<AdminAccountInfo> GetAllAdmins() => _context.Admins.Find(_ => true).ToList()
            .OrderBy(a => a.Username)
            .Select(a => new AdminAccountInfo(a.Id, a.Name, a.Username))
            .ToList();

        public List<Notification> GetNotifications(string recipientId, string recipientRole)
        {
            ValidateRecipient(recipientId, recipientRole);
            return _context.Notifications.Find(n => n.RecipientId == recipientId && n.RecipientRole == recipientRole)
                .SortByDescending(n => n.CreatedAt).ToList();
        }

        public long GetUnreadNotificationCount(string recipientId, string recipientRole)
        {
            ValidateRecipient(recipientId, recipientRole);
            return _context.Notifications.CountDocuments(n => n.RecipientId == recipientId && n.RecipientRole == recipientRole && !n.IsRead);
        }

        public bool MarkNotificationRead(string notificationId, string recipientId, string recipientRole)
        {
            ValidateRecipient(recipientId, recipientRole);
            var result = _context.Notifications.UpdateOne(
                n => n.Id == notificationId && n.RecipientId == recipientId && n.RecipientRole == recipientRole,
                Builders<Notification>.Update.Set(n => n.IsRead, true).Set(n => n.ReadAt, DateTime.UtcNow));
            return result.ModifiedCount > 0;
        }

        public bool MarkAllNotificationsRead(string recipientId, string recipientRole)
        {
            ValidateRecipient(recipientId, recipientRole);
            var result = _context.Notifications.UpdateMany(
                n => n.RecipientId == recipientId && n.RecipientRole == recipientRole && !n.IsRead,
                Builders<Notification>.Update.Set(n => n.IsRead, true).Set(n => n.ReadAt, DateTime.UtcNow));
            return result.ModifiedCount > 0;
        }

        public int SendNotification(string senderId, string senderName, string recipientRole, bool sendToAll,
            IReadOnlyCollection<string> recipientIds, string title, string message)
        {
            if (string.IsNullOrWhiteSpace(senderId) || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message))
                throw new InvalidOperationException("A notification title and message are required.");
            if (recipientRole != "User" && recipientRole != "Admin")
                throw new InvalidOperationException("The notification recipient type is invalid.");

            var validRecipientIds = recipientRole == "User"
                ? _context.Users.Find(u => u.IsActive).ToList().Select(u => u.Id).ToHashSet()
                : _context.Admins.Find(_ => true).ToList().Select(a => a.Id).ToHashSet();
            if (!sendToAll && recipientIds.Any(id => !validRecipientIds.Contains(id)))
                throw new InvalidOperationException("One or more selected recipients are no longer available.");

            var recipients = sendToAll ? validRecipientIds : recipientIds;
            var ids = recipients.Where(id => !string.IsNullOrWhiteSpace(id) && (recipientRole != "Admin" || id != senderId)).Distinct().ToList();
            if (ids.Count == 0)
                throw new InvalidOperationException("Select at least one notification recipient.");

            var now = DateTime.UtcNow;
            _context.Notifications.InsertMany(ids.Select(id => new Notification
            {
                RecipientId = id,
                RecipientRole = recipientRole,
                SenderId = senderId,
                SenderName = senderName,
                Title = title.Trim(),
                Message = message.Trim(),
                CreatedAt = now
            }));
            return ids.Count;
        }

        private static void ValidateRecipient(string recipientId, string recipientRole)
        {
            if (string.IsNullOrWhiteSpace(recipientId) || (recipientRole != "User" && recipientRole != "Admin"))
                throw new InvalidOperationException("The notification recipient is invalid.");
        }

        public MeetingSettings GetMeetingSettings()
        {
            var settings = _context.MeetingSettings.Find(s => s.Id == MeetingSettings.DefaultId).FirstOrDefault();
            if (settings != null)
                return settings;

            settings = new MeetingSettings();
            _context.MeetingSettings.InsertOne(settings);
            return settings;
        }

        public void SaveMeetingSettings(MeetingSettings settings, string adminId, string adminName)
        {
            if (!TimeSpan.TryParseExact(settings.MorningTime, @"hh\:mm", CultureInfo.InvariantCulture, out _)
                || !TimeSpan.TryParseExact(settings.EveningTime, @"hh\:mm", CultureInfo.InvariantCulture, out _))
                throw new InvalidOperationException("Meeting times must use HH:mm format.");

            if (!IsValidMeetingLink(settings.MorningMeetingLink) || !IsValidMeetingLink(settings.EveningMeetingLink))
                throw new InvalidOperationException("Meeting links must be valid http or https URLs.");

            var update = Builders<MeetingSettings>.Update
                .Set(s => s.MorningTime, settings.MorningTime)
                .Set(s => s.EveningTime, settings.EveningTime)
                .Set(s => s.MorningMeetingLink, settings.MorningMeetingLink?.Trim() ?? string.Empty)
                .Set(s => s.EveningMeetingLink, settings.EveningMeetingLink?.Trim() ?? string.Empty)
                .Set(s => s.TimeZoneId, settings.TimeZoneId)
                .Set(s => s.UpdatedAt, DateTime.UtcNow)
                .Set(s => s.UpdatedByAdminId, adminId)
                .Set(s => s.UpdatedByAdminName, adminName);

            _context.MeetingSettings.UpdateOne(
                s => s.Id == MeetingSettings.DefaultId,
                update,
                new UpdateOptions { IsUpsert = true });
        }

        public List<AttendanceRecord> GetUserAttendance(string userId, DateTime date)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return new List<AttendanceRecord>();

            EnsureDailyAttendance(date);
            return _context.Attendance.Find(a => a.UserId == userId && a.MeetingDate == FormatDate(date))
                .ToList()
                .OrderBy(a => a.MeetingType == MeetingTypes.Morning ? 0 : 1)
                .ToList();
        }

        public List<AttendanceRecord> GetAllAttendance(DateTime date)
        {
            EnsureDailyAttendance(date);
            return _context.Attendance.Find(a => a.MeetingDate == FormatDate(date)).ToList();
        }

        public bool MarkAttendancePresent(string userId, string meetingType, DateTime date)
        {
            if (string.IsNullOrWhiteSpace(userId) || !IsValidMeetingType(meetingType))
                throw new InvalidOperationException("The attendance request is invalid.");

            EnsureDailyAttendance(date);
            var settings = GetMeetingSettings();
            var now = GetApplicationNow(settings);
            if (date.Date != now.Date || !IsWithinAttendanceWindow(meetingType, settings, now))
            {
                var start = GetMeetingStart(meetingType, settings, now.Date);
                throw new InvalidOperationException(
                    $"Attendance is only available from {start:HH:mm} to {start.AddMinutes(15):HH:mm} (Sri Lanka time). Current time: {now:HH:mm} on {now:yyyy-MM-dd}; requested date: {date:yyyy-MM-dd}.");
            }

            var filter = Builders<AttendanceRecord>.Filter.And(
                Builders<AttendanceRecord>.Filter.Eq(a => a.UserId, userId),
                Builders<AttendanceRecord>.Filter.Eq(a => a.MeetingDate, FormatDate(date)),
                Builders<AttendanceRecord>.Filter.Eq(a => a.MeetingType, meetingType),
                Builders<AttendanceRecord>.Filter.Eq(a => a.Status, AttendanceStatuses.Pending));

            var update = Builders<AttendanceRecord>.Update
                .Set(a => a.Status, AttendanceStatuses.Present)
                .Set(a => a.MarkedAt, DateTime.UtcNow)
                .Set(a => a.MarkedBy, "User")
                .Set(a => a.UpdatedAt, DateTime.UtcNow);

            var result = _context.Attendance.UpdateOne(filter, update);
            if (result.ModifiedCount == 0)
                throw new InvalidOperationException("This attendance record is no longer pending or could not be found.");

            return true;
        }

        public bool UpdateAttendanceStatus(string attendanceId, string status, string adminId, string adminName, string? note)
        {
            var validStatuses = new[]
            {
                AttendanceStatuses.Present,
                AttendanceStatuses.Absent,
                AttendanceStatuses.AbsentInformed
            };
            if (string.IsNullOrWhiteSpace(attendanceId) || !validStatuses.Contains(status))
                return false;

            var update = Builders<AttendanceRecord>.Update
                .Set(a => a.Status, status)
                .Set(a => a.MarkedAt, DateTime.UtcNow)
                .Set(a => a.MarkedBy, "Admin")
                .Set(a => a.ChangedByAdminId, adminId)
                .Set(a => a.ChangedByAdminName, adminName)
                .Set(a => a.AdminNote, string.IsNullOrWhiteSpace(note) ? null : note.Trim())
                .Set(a => a.UpdatedAt, DateTime.UtcNow);

            return _context.Attendance.UpdateOne(
                Builders<AttendanceRecord>.Filter.Eq(a => a.Id, attendanceId), update).ModifiedCount > 0;
        }

        private void EnsureDailyAttendance(DateTime date)
        {
            var dateText = FormatDate(date);
            var settings = GetMeetingSettings();
            var now = GetApplicationNow(settings);
            var activeUsers = _context.Users.Find(u => u.IsActive).ToList();

            foreach (var user in activeUsers)
            {
                foreach (var meetingType in new[] { MeetingTypes.Morning, MeetingTypes.Evening })
                {
                    var filter = Builders<AttendanceRecord>.Filter.And(
                        Builders<AttendanceRecord>.Filter.Eq(a => a.UserId, user.Id),
                        Builders<AttendanceRecord>.Filter.Eq(a => a.MeetingDate, dateText),
                        Builders<AttendanceRecord>.Filter.Eq(a => a.MeetingType, meetingType));

                    var record = new AttendanceRecord
                    {
                        UserId = user.Id,
                        MeetingDate = dateText,
                        MeetingType = meetingType
                    };
                    _context.Attendance.UpdateOne(filter, Builders<AttendanceRecord>.Update
                        .SetOnInsert(a => a.UserId, record.UserId)
                        .SetOnInsert(a => a.MeetingDate, record.MeetingDate)
                        .SetOnInsert(a => a.MeetingType, record.MeetingType)
                        .SetOnInsert(a => a.Status, record.Status)
                        .SetOnInsert(a => a.CreatedAt, record.CreatedAt)
                        .SetOnInsert(a => a.UpdatedAt, record.UpdatedAt),
                        new UpdateOptions { IsUpsert = true });
                }
            }

            if (date.Date != now.Date)
                return;

            foreach (var meetingType in new[] { MeetingTypes.Morning, MeetingTypes.Evening })
            {
                if (!IsWindowClosed(meetingType, settings, now))
                    continue;

                var filter = Builders<AttendanceRecord>.Filter.And(
                    Builders<AttendanceRecord>.Filter.Eq(a => a.MeetingDate, dateText),
                    Builders<AttendanceRecord>.Filter.Eq(a => a.MeetingType, meetingType),
                    Builders<AttendanceRecord>.Filter.Eq(a => a.Status, AttendanceStatuses.Pending));
                _context.Attendance.UpdateMany(filter, Builders<AttendanceRecord>.Update
                    .Set(a => a.Status, AttendanceStatuses.Absent)
                    .Set(a => a.MarkedBy, "System")
                    .Set(a => a.UpdatedAt, DateTime.UtcNow));
            }
        }

        private static string FormatDate(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static bool IsValidMeetingType(string meetingType) =>
            meetingType == MeetingTypes.Morning || meetingType == MeetingTypes.Evening;

        private static bool IsValidMeetingLink(string? link)
        {
            return string.IsNullOrWhiteSpace(link)
                || Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
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
                var timeZone = TimeZoneInfo.FindSystemTimeZoneById(configuredTimeZone);
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.Utc).AddHours(5.5);
            }
            catch (InvalidTimeZoneException)
            {
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.Utc).AddHours(5.5);
            }
        }

        private static bool IsWithinAttendanceWindow(string meetingType, MeetingSettings settings, DateTime now)
        {
            var start = GetMeetingStart(meetingType, settings, now.Date);
            return now >= start && now <= start.AddMinutes(15);
        }

        private static bool IsWindowClosed(string meetingType, MeetingSettings settings, DateTime now)
        {
            return now > GetMeetingStart(meetingType, settings, now.Date).AddMinutes(15);
        }

        private static DateTime GetMeetingStart(string meetingType, MeetingSettings settings, DateTime date)
        {
            var time = meetingType == MeetingTypes.Morning ? settings.MorningTime : settings.EveningTime;
            if (!TimeSpan.TryParseExact(time, @"hh\:mm", CultureInfo.InvariantCulture, out var parsedTime))
                parsedTime = meetingType == MeetingTypes.Morning ? new TimeSpan(10, 0, 0) : new TimeSpan(17, 0, 0);
            return date.Add(parsedTime);
        }

        public List<TaskProject> GetProjects() => _context.Projects.Find(_ => true).SortByDescending(p => p.UpdatedAt).ToList();

        public TaskProject CreateProject(TaskProject project)
        {
            if (string.IsNullOrWhiteSpace(project.Name))
                throw new InvalidOperationException("Project name is required.");
            if (project.DueDate.Date < project.StartDate.Date)
                throw new InvalidOperationException("Project due date cannot be before its start date.");

            project.StartDate = DateTime.SpecifyKind(project.StartDate.Date, DateTimeKind.Unspecified);
            project.DueDate = DateTime.SpecifyKind(project.DueDate.Date, DateTimeKind.Unspecified);
            project.Name = project.Name.Trim();
            project.Description = project.Description?.Trim() ?? string.Empty;
            project.CreatedAt = DateTime.UtcNow;
            project.UpdatedAt = project.CreatedAt;
            _context.Projects.InsertOne(project);
            return project;
        }

        public TaskProject UpdateProject(TaskProject project)
        {
            if (string.IsNullOrWhiteSpace(project.Id) || string.IsNullOrWhiteSpace(project.Name))
                throw new InvalidOperationException("Project and project name are required.");
            if (project.DueDate.Date < project.StartDate.Date)
                throw new InvalidOperationException("Project due date cannot be before its start date.");
            if (!_context.Projects.Find(p => p.Id == project.Id).Any())
                throw new InvalidOperationException("The project could not be found.");

            project.StartDate = DateTime.SpecifyKind(project.StartDate.Date, DateTimeKind.Unspecified);
            project.DueDate = DateTime.SpecifyKind(project.DueDate.Date, DateTimeKind.Unspecified);
            project.Name = project.Name.Trim();
            project.Description = project.Description?.Trim() ?? string.Empty;
            project.UpdatedAt = DateTime.UtcNow;
            var update = Builders<TaskProject>.Update
                .Set(p => p.Name, project.Name)
                .Set(p => p.Description, project.Description)
                .Set(p => p.StartDate, project.StartDate.Date)
                .Set(p => p.DueDate, project.DueDate.Date)
                .Set(p => p.Status, project.Status)
                .Set(p => p.UpdatedAt, project.UpdatedAt);
            var result = _context.Projects.UpdateOne(p => p.Id == project.Id, update);
            if (result.MatchedCount == 0)
                throw new InvalidOperationException("The project could not be found.");
            return project;
        }

        public bool DeleteProject(string projectId)
        {
            if (string.IsNullOrWhiteSpace(projectId))
                return false;

            var componentIds = _context.Components.Find(c => c.ProjectId == projectId)
                .Project(c => c.Id)
                .ToList();
            if (componentIds.Count > 0)
            {
                _context.DailyTaskUpdates.DeleteMany(u => componentIds.Contains(u.ComponentId));
                _context.ComponentAssignments.DeleteMany(a => componentIds.Contains(a.ComponentId));
                _context.Components.DeleteMany(c => componentIds.Contains(c.Id));
            }

            return _context.Projects.DeleteOne(p => p.Id == projectId).DeletedCount > 0;
        }

        public List<TaskComponent> GetProjectComponents(string projectId) =>
            _context.Components.Find(c => c.ProjectId == projectId).SortBy(c => c.DueDate).ToList();

        public TaskComponent CreateTaskComponent(TaskComponent component)
        {
            if (string.IsNullOrWhiteSpace(component.ProjectId) || string.IsNullOrWhiteSpace(component.Name))
                throw new InvalidOperationException("Project and component name are required.");
            if (!_context.Projects.Find(p => p.Id == component.ProjectId).Any())
                throw new InvalidOperationException("The project could not be found.");

            component.DueDate = DateTime.SpecifyKind(component.DueDate.Date, DateTimeKind.Unspecified);
            component.Name = component.Name.Trim();
            component.Description = component.Description?.Trim() ?? string.Empty;
            component.CreatedAt = DateTime.UtcNow;
            component.UpdatedAt = component.CreatedAt;
            _context.Components.InsertOne(component);
            return component;
        }

        public TaskComponent UpdateTaskComponent(TaskComponent component)
        {
            if (string.IsNullOrWhiteSpace(component.Id) || string.IsNullOrWhiteSpace(component.Name))
                throw new InvalidOperationException("Component and component name are required.");
            if (!_context.Components.Find(c => c.Id == component.Id).Any())
                throw new InvalidOperationException("The component could not be found.");

            component.DueDate = DateTime.SpecifyKind(component.DueDate.Date, DateTimeKind.Unspecified);
            component.Name = component.Name.Trim();
            component.Description = component.Description?.Trim() ?? string.Empty;
            component.UpdatedAt = DateTime.UtcNow;
            var update = Builders<TaskComponent>.Update
                .Set(c => c.Name, component.Name)
                .Set(c => c.Description, component.Description)
                .Set(c => c.Priority, component.Priority)
                .Set(c => c.DueDate, component.DueDate.Date)
                .Set(c => c.Status, component.Status)
                .Set(c => c.UpdatedAt, component.UpdatedAt);
            var result = _context.Components.UpdateOne(c => c.Id == component.Id, update);
            if (result.MatchedCount == 0)
                throw new InvalidOperationException("The component could not be found.");
            return component;
        }

        public List<ComponentAssignment> GetComponentAssignments(string componentId) =>
            _context.ComponentAssignments.Find(a => a.ComponentId == componentId && a.IsActive).ToList();

        public bool SetComponentAssignments(string componentId, IReadOnlyCollection<string> userIds, string adminId)
        {
            if (!_context.Components.Find(c => c.Id == componentId).Any())
                throw new InvalidOperationException("The component could not be found.");

            var distinctUserIds = userIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToHashSet();
            var existing = _context.ComponentAssignments.Find(a => a.ComponentId == componentId).ToList();
            foreach (var assignment in existing)
            {
                var shouldBeActive = distinctUserIds.Contains(assignment.UserId);
                _context.ComponentAssignments.UpdateOne(
                    a => a.Id == assignment.Id,
                    Builders<ComponentAssignment>.Update.Set(a => a.IsActive, shouldBeActive));
            }

            foreach (var userId in distinctUserIds.Where(id => existing.All(a => a.UserId != id)))
                _context.ComponentAssignments.InsertOne(new ComponentAssignment
                {
                    ComponentId = componentId,
                    UserId = userId,
                    AssignedByAdminId = adminId
                });

            return true;
        }

        public List<AssignedTask> GetMyTasks(string userId)
        {
            var assignments = _context.ComponentAssignments.Find(a => a.UserId == userId && a.IsActive).ToList();
            var result = new List<AssignedTask>();
            foreach (var assignment in assignments)
            {
                var component = _context.Components.Find(c => c.Id == assignment.ComponentId).FirstOrDefault();
                var project = component == null ? null : _context.Projects.Find(p => p.Id == component.ProjectId).FirstOrDefault();
                if (component == null || project == null) continue;
                result.Add(new AssignedTask
                {
                    Project = project,
                    Component = component,
                    LatestUpdate = _context.DailyTaskUpdates.Find(u => u.ComponentId == component.Id && u.UserId == userId)
                        .SortByDescending(u => u.UpdateDate).FirstOrDefault()
                });
            }
            return result.OrderBy(item => item.Component.DueDate).ToList();
        }

        public List<DailyTaskUpdate> GetTaskUpdates(string componentId, string userId, bool isAdmin)
        {
            if (!isAdmin && !_context.ComponentAssignments.Find(a => a.ComponentId == componentId && a.UserId == userId && a.IsActive).Any())
                throw new InvalidOperationException("This task is not assigned to your account.");

            var filter = isAdmin
                ? Builders<DailyTaskUpdate>.Filter.Eq(u => u.ComponentId, componentId)
                : Builders<DailyTaskUpdate>.Filter.And(
                    Builders<DailyTaskUpdate>.Filter.Eq(u => u.ComponentId, componentId),
                    Builders<DailyTaskUpdate>.Filter.Eq(u => u.UserId, userId));
            return _context.DailyTaskUpdates.Find(filter).SortByDescending(u => u.UpdateDate).ToList();
        }

        public DailyTaskUpdate SaveDailyTaskUpdate(DailyTaskUpdate update)
        {
            if (string.IsNullOrWhiteSpace(update.ComponentId) || string.IsNullOrWhiteSpace(update.UserId)
                || string.IsNullOrWhiteSpace(update.Description))
                throw new InvalidOperationException("A daily work description is required.");
            if (!new[] { TaskStatuses.NotStarted, TaskStatuses.InProgress, TaskStatuses.Blocked, TaskStatuses.Completed }.Contains(update.Status))
                throw new InvalidOperationException("The selected task status is invalid.");
            if (update.Status == TaskStatuses.Blocked && string.IsNullOrWhiteSpace(update.BlockedReason))
                throw new InvalidOperationException("A blocked reason is required.");
            if (!_context.ComponentAssignments.Find(a => a.ComponentId == update.ComponentId && a.UserId == update.UserId && a.IsActive).Any())
                throw new InvalidOperationException("This task is not assigned to your account.");

            update.UpdateDate = DateTime.SpecifyKind(update.UpdateDate.Date, DateTimeKind.Unspecified);
            update.Description = update.Description.Trim();
            update.BlockedReason = string.IsNullOrWhiteSpace(update.BlockedReason) ? null : update.BlockedReason.Trim();
            update.UpdatedAt = DateTime.UtcNow;
            var filter = Builders<DailyTaskUpdate>.Filter.And(
                Builders<DailyTaskUpdate>.Filter.Eq(u => u.ComponentId, update.ComponentId),
                Builders<DailyTaskUpdate>.Filter.Eq(u => u.UserId, update.UserId),
                Builders<DailyTaskUpdate>.Filter.Eq(u => u.UpdateDate, update.UpdateDate));
            _context.DailyTaskUpdates.ReplaceOne(filter, update, new ReplaceOptions { IsUpsert = true });
            return update;
        }

        public List<ProjectDailyTaskReportRow> GetProjectDailyTaskReport(string projectId, DateTime date)
        {
            var calendarDate = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
            var project = _context.Projects.Find(p => p.Id == projectId).FirstOrDefault()
                ?? throw new InvalidOperationException("The project could not be found.");
            var components = _context.Components.Find(c => c.ProjectId == projectId).ToList();
            var users = _context.Users.Find(_ => true).ToList().ToDictionary(u => u.Id, u => u.Username ?? u.Email);
            var rows = new List<ProjectDailyTaskReportRow>();

            foreach (var component in components)
            {
                var assignments = _context.ComponentAssignments.Find(a => a.ComponentId == component.Id && a.IsActive).ToList();
                foreach (var assignment in assignments)
                {
                    var update = _context.DailyTaskUpdates.Find(u => u.ComponentId == component.Id
                        && u.UserId == assignment.UserId && u.UpdateDate == calendarDate).FirstOrDefault();
                    rows.Add(new ProjectDailyTaskReportRow
                    {
                        ProjectId = project.Id,
                        ProjectName = project.Name,
                        ComponentId = component.Id,
                        ComponentName = component.Name,
                        ComponentDescription = component.Description,
                        UserId = assignment.UserId,
                        UserName = users.TryGetValue(assignment.UserId, out var name) ? name : "Unknown member",
                        Status = update?.Status ?? "Not submitted",
                        DailyWork = update?.Description ?? "No update submitted",
                        UpdateDate = calendarDate,
                        HasSubmittedUpdate = update != null
                    });
                }
            }

            return rows.OrderBy(row => row.ComponentName).ThenBy(row => row.UserName).ToList();
        }

        public bool CanAccessUser(string targetUserId)
        {
            var activeUserId = AppSession.CurrentUserId;
            if (string.IsNullOrWhiteSpace(activeUserId) || string.IsNullOrWhiteSpace(targetUserId))
                return false;

            return string.Equals(activeUserId, targetUserId, StringComparison.Ordinal);
        }

        public bool EmailExists(string email)
        {
            var normalizedEmail = SecurityValidator.NormalizeEmail(email);
            return _context.Users.Find(u => u.Email == normalizedEmail).Any();
        }

        public bool UsernameExists(string username)
        {
            var normalizedUsername = SecurityValidator.NormalizeUsername(username);
            return _context.Users.Find(u => u.Username == normalizedUsername).Any();
        }
    }
}
