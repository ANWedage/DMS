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

        public List<TaskComponent> GetProjectComponents(string projectId) =>
            _context.Components.Find(c => c.ProjectId == projectId).SortBy(c => c.DueDate).ToList();

        public TaskComponent CreateTaskComponent(TaskComponent component)
        {
            if (string.IsNullOrWhiteSpace(component.ProjectId) || string.IsNullOrWhiteSpace(component.Name))
                throw new InvalidOperationException("Project and component name are required.");
            if (!_context.Projects.Find(p => p.Id == component.ProjectId).Any())
                throw new InvalidOperationException("The project could not be found.");

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

            update.UpdateDate = update.UpdateDate.Date;
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
