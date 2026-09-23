using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;
using System.Globalization;
using System.IO;
using DMS.Data;
using DMS.Helpers;
using DMS.Models;

namespace DMS.Services
{
    public class UserService : IUserService
    {
        private readonly MongoDbContext _context;
        private static readonly string[] AdminAttendanceMeetingTypes = { MeetingTypes.Morning, MeetingTypes.Evening };
        private static readonly TimeSpan AdminAttendanceCutoff = new(17, 30, 0);

        public UserService(MongoDbContext context)
        {
            _context = context;
        }

            public List<ChatUser> GetChatUsers(string currentUserId, string currentRole)
            {
                ValidateChatIdentity(currentUserId, currentRole);
                var users = _context.Users.Find(u => u.IsActive).ToList()
                    .Where(u => !string.Equals(u.Id, currentUserId, StringComparison.Ordinal))
                    .Select(u => new ChatUser(u.Id, u.Username ?? u.Email, "User"));
                var admins = _context.Admins.Find(_ => true).ToList()
                    .Where(a => !string.Equals(a.Id, currentUserId, StringComparison.Ordinal))
                    .Select(a => new ChatUser(a.Id, string.IsNullOrWhiteSpace(a.Name) ? a.Username : a.Name, "Admin"));
                return users.Concat(admins).OrderBy(u => u.DisplayName).ToList();
            }

            public List<ChatConversationSummary> GetChatInbox(string currentUserId, string currentRole) =>
                GetChatConversations(currentUserId, currentRole, received: true);

            public List<ChatConversationSummary> GetChatSent(string currentUserId, string currentRole) =>
                GetChatConversations(currentUserId, currentRole, received: false);

            public long GetUnreadChatCount(string currentUserId, string currentRole)
            {
                ValidateChatIdentity(currentUserId, currentRole);
                return _context.ChatMessages.CountDocuments(m =>
                    m.RecipientId == currentUserId && m.RecipientRole == currentRole && m.ReadAt == null);
            }

            public List<ChatMessage> GetChatMessages(string currentUserId, string currentRole, string otherUserId, string otherRole)
            {
                ValidateChatIdentity(currentUserId, currentRole);
                ValidateChatParticipant(otherUserId, otherRole);
                var conversationKey = BuildConversationKey(currentUserId, currentRole, otherUserId, otherRole);
                return _context.ChatMessages.Find(m => m.ConversationKey == conversationKey)
                    .SortBy(m => m.CreatedAt).ToList();
            }

            public bool DeleteChatConversation(string currentUserId, string currentRole, string otherUserId, string otherRole)
            {
                ValidateChatIdentity(currentUserId, currentRole);
                ValidateChatParticipant(otherUserId, otherRole);
                var conversationKey = BuildConversationKey(currentUserId, currentRole, otherUserId, otherRole);
                return _context.ChatMessages.DeleteMany(m => m.ConversationKey == conversationKey).DeletedCount > 0;
            }

            public ChatAttachment? GetChatAttachment(string currentUserId, string currentRole, string attachmentId)
            {
                ValidateChatIdentity(currentUserId, currentRole);
                if (string.IsNullOrWhiteSpace(attachmentId))
                    return null;

                var attachment = _context.ChatAttachments.Find(a => a.Id == attachmentId && !a.IsDeleted).FirstOrDefault();
                if (attachment == null)
                    return null;

                var isSender = string.Equals(attachment.SenderId, currentUserId, StringComparison.Ordinal) && string.Equals(attachment.SenderRole, currentRole, StringComparison.Ordinal);
                var isRecipient = string.Equals(attachment.RecipientId, currentUserId, StringComparison.Ordinal) && string.Equals(attachment.RecipientRole, currentRole, StringComparison.Ordinal);
                if (!isSender && !isRecipient)
                    throw new InvalidOperationException("You do not have access to this attachment.");

                return attachment;
            }

            public ChatAttachmentDownloadResponse DownloadChatAttachment(string currentUserId, string currentRole, string attachmentId)
            {
                ValidateChatIdentity(currentUserId, currentRole);
                var attachment = GetChatAttachment(currentUserId, currentRole, attachmentId);
                if (attachment == null)
                    throw new InvalidOperationException("The attachment could not be found.");

                byte[] payload;
                if (!string.IsNullOrWhiteSpace(attachment.StorageObjectId) && ObjectId.TryParse(attachment.StorageObjectId, out var storageObjectId))
                {
                    payload = _context.ChatAttachmentsBucket.DownloadAsBytes(storageObjectId);
                }
                else
                {
                    var storagePath = Path.Combine(AppContext.BaseDirectory, "ChatUploads", attachment.StoredFileName);
                    if (!File.Exists(storagePath))
                        throw new InvalidOperationException("The PDF file is no longer available on disk.");
                    payload = File.ReadAllBytes(storagePath);
                }

                return new ChatAttachmentDownloadResponse(
                    attachment.Id,
                    attachment.OriginalFileName,
                    Convert.ToBase64String(payload),
                    payload.LongLength);
            }

            public ChatAttachment UploadChatAttachment(string senderId, string senderRole, string recipientId, string recipientRole, string fileName, byte[] content)
            {
                ValidateChatIdentity(senderId, senderRole);
                ValidateChatParticipant(recipientId, recipientRole);
                if (string.Equals(senderId, recipientId, StringComparison.Ordinal) && senderRole == recipientRole)
                    throw new InvalidOperationException("You cannot send a file to yourself.");

                var trimmedFileName = (fileName ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(trimmedFileName) || !trimmedFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Only PDF files can be shared in chat.");

                var normalizedContent = content ?? Array.Empty<byte>();
                if (normalizedContent.Length == 0 || normalizedContent.Length > 10 * 1024 * 1024)
                    throw new InvalidOperationException("The PDF file must be larger than zero and no more than 10 MB.");

                var attachmentId = ObjectId.GenerateNewId().ToString();
                var storedFileName = $"{attachmentId}{Path.GetExtension(trimmedFileName)}";
                var storageObjectId = _context.ChatAttachmentsBucket.UploadFromBytes(storedFileName, normalizedContent, new GridFSUploadOptions
                {
                    Metadata = new BsonDocument
                    {
                        ["AttachmentId"] = attachmentId,
                        ["ConversationKey"] = BuildConversationKey(senderId, senderRole, recipientId, recipientRole),
                        ["SenderId"] = senderId,
                        ["RecipientId"] = recipientId,
                        ["CreatedAt"] = DateTime.UtcNow
                    }
                });

                var attachment = new ChatAttachment
                {
                    Id = attachmentId,
                    ConversationKey = BuildConversationKey(senderId, senderRole, recipientId, recipientRole),
                    SenderId = senderId,
                    SenderRole = senderRole,
                    RecipientId = recipientId,
                    RecipientRole = recipientRole,
                    OriginalFileName = trimmedFileName,
                    StoredFileName = storedFileName,
                    StorageObjectId = storageObjectId.ToString(),
                    FileSizeBytes = normalizedContent.Length,
                    ContentType = "application/pdf",
                    CreatedAt = DateTime.UtcNow,
                    IsDeleted = false
                };

                _context.ChatAttachments.InsertOne(attachment);
                return attachment;
            }

            public bool DeleteChatAttachment(string currentUserId, string currentRole, string attachmentId)
            {
                ValidateChatIdentity(currentUserId, currentRole);
                if (string.IsNullOrWhiteSpace(attachmentId))
                    return false;

                var attachment = _context.ChatAttachments.Find(a => a.Id == attachmentId).FirstOrDefault();
                if (attachment == null)
                    return false;

                var isSender = string.Equals(attachment.SenderId, currentUserId, StringComparison.Ordinal) && string.Equals(attachment.SenderRole, currentRole, StringComparison.Ordinal);
                if (!isSender)
                    throw new InvalidOperationException("Only the sender can delete this attachment.");

                if (attachment.IsDeleted)
                    return true;

                if (!string.IsNullOrWhiteSpace(attachment.StorageObjectId) && ObjectId.TryParse(attachment.StorageObjectId, out var storageObjectId))
                {
                    try
                    {
                        _context.ChatAttachmentsBucket.Delete(storageObjectId);
                    }
                    catch (Exception)
                    {
                        // File may already be absent; continue to mark metadata as deleted.
                    }
                }
                else
                {
                    var storageFile = Path.Combine(AppContext.BaseDirectory, "ChatUploads", attachment.StoredFileName);
                    if (File.Exists(storageFile))
                        File.Delete(storageFile);
                }

                attachment.IsDeleted = true;
                attachment.DeletedAt = DateTime.UtcNow;
                _context.ChatAttachments.ReplaceOne(a => a.Id == attachmentId, attachment);
                return true;
            }

            public ChatMessage SaveChatMessage(string senderId, string senderRole, string recipientId, string recipientRole, string messageText)
                => SaveChatMessage(senderId, senderRole, recipientId, recipientRole, messageText, null);

            public ChatMessage SaveChatMessage(string senderId, string senderRole, string recipientId, string recipientRole, string messageText, string? attachmentId = null)
            {
                ValidateChatIdentity(senderId, senderRole);
                ValidateChatParticipant(recipientId, recipientRole);
                if (string.Equals(senderId, recipientId, StringComparison.Ordinal) && senderRole == recipientRole)
                    throw new InvalidOperationException("You cannot send a chat message to yourself.");

                if (!string.IsNullOrWhiteSpace(attachmentId))
                {
                    var attachment = _context.ChatAttachments.Find(a => a.Id == attachmentId).FirstOrDefault();
                    if (attachment == null)
                        throw new InvalidOperationException("The attachment could not be found.");
                    if (attachment.IsDeleted)
                        throw new InvalidOperationException("The attachment has already been deleted.");
                    if (!string.Equals(attachment.SenderId, senderId, StringComparison.Ordinal) || !string.Equals(attachment.RecipientId, recipientId, StringComparison.Ordinal))
                        throw new InvalidOperationException("The attachment does not match this conversation.");
                }

                var text = messageText?.Trim() ?? string.Empty;
                if (text.Length == 0 && string.IsNullOrWhiteSpace(attachmentId))
                    throw new InvalidOperationException("Chat messages must contain between 1 and 4000 characters.");

                if (text.Length == 0 && !string.IsNullOrWhiteSpace(attachmentId))
                    text = "Send attachment";

                if (text.Length > 4000)
                    throw new InvalidOperationException("Chat messages must contain between 1 and 4000 characters.");

                var message = new ChatMessage
                {
                    ConversationKey = BuildConversationKey(senderId, senderRole, recipientId, recipientRole),
                    SenderId = senderId,
                    SenderRole = senderRole,
                    RecipientId = recipientId,
                    RecipientRole = recipientRole,
                    MessageText = text,
                    AttachmentId = attachmentId
                };
                _context.ChatMessages.InsertOne(message);
                return message;
            }

            public bool MarkChatMessagesRead(string recipientId, string recipientRole, string senderId, string senderRole)
            {
                ValidateChatIdentity(recipientId, recipientRole);
                ValidateChatParticipant(senderId, senderRole);
                var filter = Builders<ChatMessage>.Filter.And(
                    Builders<ChatMessage>.Filter.Eq(m => m.RecipientId, recipientId),
                    Builders<ChatMessage>.Filter.Eq(m => m.RecipientRole, recipientRole),
                    Builders<ChatMessage>.Filter.Eq(m => m.SenderId, senderId),
                    Builders<ChatMessage>.Filter.Eq(m => m.SenderRole, senderRole),
                    Builders<ChatMessage>.Filter.Eq(m => m.ReadAt, null));
                var result = _context.ChatMessages.UpdateMany(filter,
                    Builders<ChatMessage>.Update.Set(m => m.ReadAt, DateTime.UtcNow));
                return result.ModifiedCount > 0;
            }

            private List<ChatConversationSummary> GetChatConversations(string currentUserId, string currentRole, bool received)
            {
                ValidateChatIdentity(currentUserId, currentRole);
                var messages = _context.ChatMessages.Find(received
                    ? Builders<ChatMessage>.Filter.Eq(m => m.RecipientId, currentUserId) & Builders<ChatMessage>.Filter.Eq(m => m.RecipientRole, currentRole)
                    : Builders<ChatMessage>.Filter.Eq(m => m.SenderId, currentUserId) & Builders<ChatMessage>.Filter.Eq(m => m.SenderRole, currentRole))
                    .SortByDescending(m => m.CreatedAt).ToList();

                return messages.GroupBy(m => (Id: received ? m.SenderId : m.RecipientId, Role: received ? m.SenderRole : m.RecipientRole))
                    .Select(group =>
                    {
                        var latest = group.First();
                        var displayName = FindChatDisplayName(group.Key.Id, group.Key.Role);
                        return new ChatConversationSummary(group.Key.Id, displayName, group.Key.Role,
                            latest.MessageText, latest.CreatedAt,
                            received ? group.Count(m => m.ReadAt == null) : 0);
                    })
                    .OrderByDescending(summary => summary.LatestMessageAt)
                    .ToList();
            }

            private string FindChatDisplayName(string id, string role)
            {
                if (role == "Admin")
                {
                    var admin = _context.Admins.Find(a => a.Id == id).FirstOrDefault();
                    return admin == null || string.IsNullOrWhiteSpace(admin.Name) ? admin?.Username ?? "Unknown" : admin.Name;
                }

                var user = _context.Users.Find(u => u.Id == id).FirstOrDefault();
                return user == null ? "Unknown" : user.Username ?? user.Email;
            }

            private void ValidateChatIdentity(string id, string role)
            {
                ValidateChatParticipant(id, role);
            }

            private void ValidateChatParticipant(string id, string role)
            {
                if (string.IsNullOrWhiteSpace(id) || (role != "User" && role != "Admin"))
                    throw new InvalidOperationException("The chat participant is invalid.");

                if (role == "Admin")
                {
                    if (!_context.Admins.Find(a => a.Id == id).Any())
                        throw new InvalidOperationException("The chat participant could not be found.");
                }
                else if (!_context.Users.Find(u => u.Id == id && u.IsActive).Any())
                {
                    throw new InvalidOperationException("The chat participant is not available.");
                }
            }

            private static string BuildConversationKey(string firstId, string firstRole, string secondId, string secondRole)
            {
                var first = $"{firstRole}:{firstId}";
                var second = $"{secondRole}:{secondId}";
                return string.CompareOrdinal(first, second) < 0 ? $"{first}|{second}" : $"{second}|{first}";
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

        public bool UsernameExistsForRole(string username, bool isAdmin)
        {
            var normalizedUsername = SecurityValidator.NormalizeUsername(username);
            if (!SecurityValidator.IsValidUsername(normalizedUsername))
                return false;

            if (isAdmin)
                return _context.Admins.Find(a => a.Username == normalizedUsername).Any();

            return _context.Users.Find(u => u.Username == normalizedUsername).Any();
        }

        public bool ResetPassword(string username, string newPassword, bool isAdmin)
        {
            var normalizedUsername = SecurityValidator.NormalizeUsername(username);
            if (!SecurityValidator.IsValidUsername(normalizedUsername))
                throw new InvalidOperationException("Username is invalid.");

            if (!SecurityValidator.IsStrongPassword(newPassword))
                throw new InvalidOperationException("Password must be at least 8 characters and include uppercase, lowercase, a number, and a symbol.");

            var (hash, salt) = PasswordHasher.HashPassword(newPassword);

            if (isAdmin)
            {
                var result = _context.Admins.UpdateOne(
                    a => a.Username == normalizedUsername,
                    Builders<AdminUser>.Update
                        .Set(a => a.PasswordHash, hash)
                        .Set(a => a.PasswordSalt, salt));

                return result.ModifiedCount > 0;
            }

            var userResult = _context.Users.UpdateOne(
                u => u.Username == normalizedUsername,
                Builders<User>.Update
                    .Set(u => u.PasswordHash, hash)
                    .Set(u => u.PasswordSalt, salt));

            return userResult.ModifiedCount > 0;
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

        public bool SetUserPosition(string userId, string? position)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return false;

            var normalizedPosition = User.NormalizePosition(position);
            if (!User.IsSupportedPosition(normalizedPosition))
                return false;

            var result = _context.Users.UpdateOne(
                u => u.Id == userId,
                Builders<User>.Update.Set(u => u.Position, normalizedPosition));

            return result.ModifiedCount > 0;
        }

        public bool SetUserLeavingDate(string userId, DateTime? leavingDate)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return false;

            var result = _context.Users.UpdateOne(
                u => u.Id == userId,
                Builders<User>.Update.Set(u => u.LeavingDate, leavingDate?.Date));

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

        public UserAccountInfo UpdateUserProfile(string userId, UserProfileUpdate update)
        {
            var user = _context.Users.Find(u => u.Id == userId).FirstOrDefault();
            if (user == null || !string.Equals(user.Username, SecurityValidator.NormalizeUsername(update.CurrentUsername), StringComparison.Ordinal)
                || !PasswordHasher.Verify(update.CurrentPassword ?? string.Empty, user.PasswordHash, user.PasswordSalt))
                throw new InvalidOperationException("The current username or password is incorrect.");

            var normalizedUsername = SecurityValidator.NormalizeUsername(update.NewUsername);
            if (!SecurityValidator.IsValidUsername(normalizedUsername))
                throw new InvalidOperationException("Username must be 3-20 characters, letters/numbers/underscore only, and contain no spaces.");
            if (_context.Users.Find(u => u.Username == normalizedUsername && u.Id != userId).Any())
                throw new InvalidOperationException("That username is already taken.");
            if (!SecurityValidator.IsStrongPassword(update.NewPassword))
                throw new InvalidOperationException("Password must be at least 8 characters and include uppercase, lowercase, a number, and a symbol.");

            var (hash, salt) = PasswordHasher.HashPassword(update.NewPassword);
            _context.Users.UpdateOne(u => u.Id == userId,
                Builders<User>.Update.Set(u => u.Username, normalizedUsername)
                    .Set(u => u.PasswordHash, hash)
                    .Set(u => u.PasswordSalt, salt));
            return new UserAccountInfo(user.Id, normalizedUsername, user.Email, user.ContactNumber);
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

        public AdminAccountInfo UpdateAdminProfile(string adminId, AdminProfileUpdate update)
        {
            var admin = GetAdminForUpdate(adminId, update.CurrentUsername, update.CurrentPassword);
            var normalizedUsername = SecurityValidator.NormalizeUsername(update.NewUsername);
            if (!SecurityValidator.IsValidUsername(normalizedUsername))
                throw new InvalidOperationException("Username must be 3-20 characters, letters/numbers/underscore only, and contain no spaces.");
            if (_context.Admins.Find(a => a.Username == normalizedUsername && a.Id != adminId).Any())
                throw new InvalidOperationException("That username is already taken.");
            if (!SecurityValidator.IsStrongPassword(update.NewPassword))
                throw new InvalidOperationException("Password must be at least 8 characters and include uppercase, lowercase, a number, and a symbol.");

            var (hash, salt) = PasswordHasher.HashPassword(update.NewPassword);
            _context.Admins.UpdateOne(a => a.Id == adminId,
                Builders<AdminUser>.Update.Set(a => a.Username, normalizedUsername)
                    .Set(a => a.PasswordHash, hash)
                    .Set(a => a.PasswordSalt, salt));
            return new AdminAccountInfo(admin.Id, admin.Name, normalizedUsername);
        }

        private AdminUser GetAdminForUpdate(string adminId, string currentUsername, string currentPassword)
        {
            var admin = _context.Admins.Find(a => a.Id == adminId).FirstOrDefault();
            if (admin == null || !string.Equals(admin.Username, SecurityValidator.NormalizeUsername(currentUsername), StringComparison.Ordinal)
                || !PasswordHasher.Verify(currentPassword ?? string.Empty, admin.PasswordHash, admin.PasswordSalt))
                throw new InvalidOperationException("The current username or password is incorrect.");
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

        public List<DeveloperDailyTaskStatus> GetDeveloperDailyTaskStatus(DateTime date)
        {
            var calendarDate = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
            var taskFilter = Builders<DailyTaskUpdate>.Filter.Eq(update => update.UpdateDate, calendarDate);
            var submittedUserIds = _context.DailyTaskUpdates
                .Distinct(update => update.UserId, taskFilter)
                .ToList()
                .ToHashSet(StringComparer.Ordinal);

            var attendanceFilter = Builders<AttendanceRecord>.Filter.Eq(record => record.MeetingDate, FormatDate(date));
            var attendanceByUser = _context.Attendance
                .Find(attendanceFilter)
                .ToList()
                .Where(record => !string.IsNullOrWhiteSpace(record.UserId))
                .GroupBy(record => record.UserId, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Select(record => record.Status).ToList(), StringComparer.Ordinal);

            var userIds = submittedUserIds
                .Concat(attendanceByUser.Keys)
                .Where(userId => !string.IsNullOrWhiteSpace(userId))
                .Distinct(StringComparer.Ordinal);

            return userIds.Select(userId =>
            {
                var hasSubmittedUpdate = submittedUserIds.Contains(userId);
                var statuses = attendanceByUser.GetValueOrDefault(userId) ?? new List<string>();
                var leaveCount = statuses.Count(status => status == AttendanceStatuses.Leave);
                var presentCount = statuses.Count(status => status == AttendanceStatuses.Present);
                var displayStatus = leaveCount >= 2
                    ? "Leave"
                    : presentCount == 1 && leaveCount == 1
                        ? hasSubmittedUpdate ? "Submitted" : "Half day"
                        : hasSubmittedUpdate ? "Submitted" : "Not submitted";

                return new DeveloperDailyTaskStatus
                {
                    UserId = userId,
                    HasSubmittedUpdate = hasSubmittedUpdate,
                    DisplayStatus = displayStatus
                };
            }).ToList();
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
            EnsureCurrentDailyReminderIfDue();
            return _context.Notifications.Find(n => n.RecipientId == recipientId && n.RecipientRole == recipientRole)
                .SortByDescending(n => n.CreatedAt).ToList();
        }

        private void EnsureCurrentDailyReminderIfDue()
        {
            var settings = GetMeetingSettings();
            var now = GetApplicationNow(settings);
            if (now.TimeOfDay >= TimeSpan.FromHours(16).Add(TimeSpan.FromMinutes(50))
                && now.TimeOfDay < TimeSpan.FromHours(17).Add(TimeSpan.FromMinutes(10)))
                EnsureDailyTaskReminder(now.Date);
        }

        public List<Notification> GetSentNotifications(string senderId, string senderRole)
        {
            if (string.IsNullOrWhiteSpace(senderId) || senderRole != "Admin")
                throw new InvalidOperationException("Only administrators can view sent notification history.");

            return _context.Notifications.Find(n => n.SenderId == senderId)
                .SortByDescending(n => n.CreatedAt)
                .ToList()
                .Select(notification =>
                {
                    notification.IsSent = true;
                    return notification;
                })
                .ToList();
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

        public void EnsureDailyTaskReminder(DateTime localDate)
        {
            var reminderKey = $"daily-task-reminder:{localDate:yyyy-MM-dd}";
            var recipients = _context.Users.Find(u => u.IsActive).ToList()
                .Select(user => (Id: user.Id, Role: "User", Name: user.Username ?? user.Email))
                .Concat(_context.Admins.Find(_ => true).ToList()
                    .Select(admin => (Id: admin.Id, Role: "Admin", Name: string.IsNullOrWhiteSpace(admin.Name) ? admin.Username : admin.Name)))
                .ToList();

            foreach (var recipient in recipients)
            {
                var filter = Builders<Notification>.Filter.And(
                    Builders<Notification>.Filter.Eq(n => n.RecipientId, recipient.Id),
                    Builders<Notification>.Filter.Eq(n => n.ReminderKey, reminderKey));
                _context.Notifications.UpdateOne(filter,
                    Builders<Notification>.Update
                        .SetOnInsert(n => n.RecipientId, recipient.Id)
                        .SetOnInsert(n => n.RecipientRole, recipient.Role)
                        .SetOnInsert(n => n.RecipientName, recipient.Name)
                        .SetOnInsert(n => n.SenderId, "System")
                        .SetOnInsert(n => n.SenderName, "DMS Task Reminder")
                        .SetOnInsert(n => n.Title, "Daily task update reminder")
                        .SetOnInsert(n => n.Message, "Please complete and submit your daily task update before the workday ends.")
                        .SetOnInsert(n => n.CreatedAt, DateTime.UtcNow)
                        .SetOnInsert(n => n.IsRead, false)
                        .SetOnInsert(n => n.IsHighPriority, true)
                        .SetOnInsert(n => n.ReminderKey, reminderKey),
                    new UpdateOptions { IsUpsert = true });

            }
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

            var recipientNames = recipientRole == "User"
                ? _context.Users.Find(u => ids.Contains(u.Id)).ToList()
                    .ToDictionary(u => u.Id, u => u.Username ?? u.Email)
                : _context.Admins.Find(a => ids.Contains(a.Id)).ToList()
                    .ToDictionary(a => a.Id, a => string.IsNullOrWhiteSpace(a.Name) ? a.Username : a.Name);

            var now = DateTime.UtcNow;
            _context.Notifications.InsertMany(ids.Select(id => new Notification
            {
                RecipientId = id,
                RecipientRole = recipientRole,
                RecipientName = recipientNames.TryGetValue(id, out var recipientName) ? recipientName : id,
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
            {
                settings.EnsureTeamSettings();
                return settings;
            }

            settings = new MeetingSettings();
            settings.EnsureTeamSettings();
            _context.MeetingSettings.InsertOne(settings);
            return settings;
        }

        public MeetingSettings GetMeetingSettingsForUser(string userId)
        {
            var user = GetUserById(userId);
            var settings = GetMeetingSettings();
            var teamSettings = settings.GetTeamSettings(user.Position);
            settings.MorningTime = teamSettings.MorningTime;
            settings.EveningTime = teamSettings.EveningTime;
            settings.MorningMeetingLink = teamSettings.MorningMeetingLink;
            settings.EveningMeetingLink = teamSettings.EveningMeetingLink;
            return settings;
        }

        public void SaveMeetingSettings(MeetingSettings settings, string adminId, string adminName)
        {
            settings.EnsureTeamSettings();
            if (!IsValidTeamSettings(settings.FullStack!)
                || !IsValidTeamSettings(settings.QA!)
                || !IsValidTeamSettings(settings.UIUX!)
                || !TimeSpan.TryParseExact(settings.WeeklyTime, @"hh\:mm", CultureInfo.InvariantCulture, out _))
                throw new InvalidOperationException("Meeting times must use HH:mm format.");

            if (!IsValidMeetingLink(settings.FullStack!.MorningMeetingLink) || !IsValidMeetingLink(settings.FullStack.EveningMeetingLink)
                || !IsValidMeetingLink(settings.QA!.MorningMeetingLink) || !IsValidMeetingLink(settings.QA.EveningMeetingLink)
                || !IsValidMeetingLink(settings.UIUX!.MorningMeetingLink) || !IsValidMeetingLink(settings.UIUX.EveningMeetingLink)
                || !IsValidMeetingLink(settings.WeeklyMeetingLink))
                throw new InvalidOperationException("Meeting links must be valid http or https URLs.");
            if (!IsValidMeetingLink(settings.DailyTaskFormLink) || !IsValidMeetingLink(settings.LeaveFormLink))
                throw new InvalidOperationException("Daily task and leave form links must be valid http or https URLs.");

            var update = Builders<MeetingSettings>.Update
                .Set(s => s.MorningTime, settings.MorningTime)
                .Set(s => s.EveningTime, settings.EveningTime)
                .Set(s => s.FullStack, settings.FullStack)
                .Set(s => s.QA, settings.QA)
                .Set(s => s.UIUX, settings.UIUX)
                .Set(s => s.WeeklyTime, settings.WeeklyTime)
                .Set(s => s.MorningMeetingLink, settings.MorningMeetingLink?.Trim() ?? string.Empty)
                .Set(s => s.EveningMeetingLink, settings.EveningMeetingLink?.Trim() ?? string.Empty)
                .Set(s => s.WeeklyMeetingLink, settings.WeeklyMeetingLink?.Trim() ?? string.Empty)
                .Set(s => s.DailyTaskFormLink, settings.DailyTaskFormLink?.Trim() ?? string.Empty)
                .Set(s => s.LeaveFormLink, settings.LeaveFormLink?.Trim() ?? string.Empty)
                .Set(s => s.TimeZoneId, settings.TimeZoneId)
                .Set(s => s.UpdatedAt, DateTime.UtcNow)
                .Set(s => s.UpdatedByAdminId, adminId)
                .Set(s => s.UpdatedByAdminName, adminName);

            _context.MeetingSettings.UpdateOne(
                s => s.Id == MeetingSettings.DefaultId,
                update,
                new UpdateOptions { IsUpsert = true });
        }

        private static bool IsValidTeamSettings(TeamMeetingSettings settings) =>
            TimeSpan.TryParseExact(settings.MorningTime, @"hh\:mm", CultureInfo.InvariantCulture, out _)
            && TimeSpan.TryParseExact(settings.EveningTime, @"hh\:mm", CultureInfo.InvariantCulture, out _);

        public List<AttendanceRecord> GetUserAttendance(string userId, DateTime date)
        {
            if (string.IsNullOrWhiteSpace(userId))
                return new List<AttendanceRecord>();

            if (!MeetingSchedule.IsWorkingDay(date))
                return new List<AttendanceRecord>();

            var user = GetUserById(userId);
            if (!User.IsSupportedPosition(user.Position))
                return new List<AttendanceRecord>();

            EnsureUserDailyAttendance(user, date);
            var settings = GetMeetingSettings();
            var activeTypes = MeetingSchedule.ForDate(settings, date, user.Position).Select(slot => slot.Type).ToHashSet();
            return _context.Attendance.Find(a => a.UserId == userId && a.MeetingDate == FormatDate(date))
                .ToList()
                .Where(a => activeTypes.Contains(a.MeetingType))
                .OrderBy(a => activeTypes.ToList().IndexOf(a.MeetingType))
                .ToList();
        }

        public List<AttendanceRecord> GetAllAttendance(DateTime date)
        {
            if (!MeetingSchedule.IsWorkingDay(date))
                return new List<AttendanceRecord>();

            EnsureDailyAttendance(date);
            return _context.Attendance.Find(a => a.MeetingDate == FormatDate(date)).ToList();
        }

        public bool MarkAttendancePresent(string userId, string meetingType, DateTime date)
        {
            if (string.IsNullOrWhiteSpace(userId) || !IsValidMeetingType(meetingType))
                throw new InvalidOperationException("The attendance request is invalid.");
            if (!MeetingSchedule.IsWorkingDay(date))
                throw new InvalidOperationException("Attendance is not available on weekends because they are non-working days.");

            var user = GetUserById(userId);
            if (!User.IsSupportedPosition(user.Position))
                throw new InvalidOperationException("Your team has not been assigned yet. Please contact an administrator.");

            EnsureUserDailyAttendance(user, date);
            var settings = GetMeetingSettings();
            var now = GetApplicationNow(settings);
            if (date.Date != now.Date || !MeetingSchedule.ForDate(settings, date, user.Position).Any(slot => slot.Type == meetingType)
                || !IsWithinAttendanceWindow(meetingType, settings, user.Position, now))
            {
                var start = GetMeetingStart(meetingType, settings, user.Position, now.Date);
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
                AttendanceStatuses.AbsentInformed,
                AttendanceStatuses.Leave
            };
            if (string.IsNullOrWhiteSpace(attendanceId) || !validStatuses.Contains(status))
                return false;

            var existing = _context.Attendance.Find(a => a.Id == attendanceId).FirstOrDefault();
            if (existing == null || !DateTime.TryParseExact(existing.MeetingDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var meetingDate)
                || !MeetingSchedule.IsWorkingDay(meetingDate))
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

        public List<AdminAttendanceRecord> GetAdminAttendance(string adminId, DateTime date)
        {
            ValidateAdminId(adminId);
            if (!MeetingSchedule.IsWorkingDay(date))
                return new List<AdminAttendanceRecord>();

            var settings = GetMeetingSettings();
            var now = GetApplicationNow(settings);
            var dateText = FormatDate(date);
            foreach (var meetingType in AdminAttendanceMeetingTypes)
            {
                var filter = Builders<AdminAttendanceRecord>.Filter.And(
                    Builders<AdminAttendanceRecord>.Filter.Eq(a => a.AdminId, adminId),
                    Builders<AdminAttendanceRecord>.Filter.Eq(a => a.MeetingDate, dateText),
                    Builders<AdminAttendanceRecord>.Filter.Eq(a => a.MeetingType, meetingType));
                _context.AdminAttendance.UpdateOne(
                    filter,
                    Builders<AdminAttendanceRecord>.Update
                        .SetOnInsert(a => a.AdminId, adminId)
                        .SetOnInsert(a => a.MeetingDate, dateText)
                        .SetOnInsert(a => a.MeetingType, meetingType)
                        .SetOnInsert(a => a.Status, AttendanceStatuses.Pending)
                        .SetOnInsert(a => a.CreatedAt, DateTime.UtcNow)
                        .Set(a => a.UpdatedAt, DateTime.UtcNow),
                    new UpdateOptions { IsUpsert = true });
            }

            if (date.Date < now.Date || date.Date == now.Date && now.TimeOfDay >= AdminAttendanceCutoff)
            {
                var pendingFilter = Builders<AdminAttendanceRecord>.Filter.And(
                    Builders<AdminAttendanceRecord>.Filter.Eq(a => a.AdminId, adminId),
                    Builders<AdminAttendanceRecord>.Filter.Eq(a => a.MeetingDate, dateText),
                    Builders<AdminAttendanceRecord>.Filter.Eq(a => a.Status, AttendanceStatuses.Pending));
                _context.AdminAttendance.UpdateMany(
                    pendingFilter,
                    Builders<AdminAttendanceRecord>.Update
                        .Set(a => a.Status, AttendanceStatuses.Absent)
                        .Set(a => a.MarkedAt, DateTime.UtcNow)
                        .Set(a => a.MarkedBy, "System")
                        .Set(a => a.UpdatedAt, DateTime.UtcNow));
            }

            return _context.AdminAttendance.Find(a => a.AdminId == adminId && a.MeetingDate == dateText)
                .ToList()
                .OrderBy(record => Array.IndexOf(AdminAttendanceMeetingTypes, record.MeetingType))
                .ToList();
        }

        public bool MarkAdminAttendancePresent(string adminId, string meetingType, DateTime date)
        {
            ValidateAdminId(adminId);
            if (!AdminAttendanceMeetingTypes.Contains(meetingType) || !MeetingSchedule.IsWorkingDay(date))
                throw new InvalidOperationException("The admin attendance request is invalid.");

            var settings = GetMeetingSettings();
            var now = GetApplicationNow(settings);
            if (date.Date != now.Date || now.TimeOfDay >= AdminAttendanceCutoff)
            {
                throw new InvalidOperationException(
                    $"Admin attendance can be marked only today before 17:30. Current time: {now:HH:mm}.");
            }

            GetAdminAttendance(adminId, date);
            var filter = Builders<AdminAttendanceRecord>.Filter.And(
                Builders<AdminAttendanceRecord>.Filter.Eq(a => a.AdminId, adminId),
                Builders<AdminAttendanceRecord>.Filter.Eq(a => a.MeetingDate, FormatDate(date)),
                Builders<AdminAttendanceRecord>.Filter.Eq(a => a.MeetingType, meetingType),
                Builders<AdminAttendanceRecord>.Filter.Eq(a => a.Status, AttendanceStatuses.Pending));
            var update = Builders<AdminAttendanceRecord>.Update
                .Set(a => a.Status, AttendanceStatuses.Present)
                .Set(a => a.MarkedAt, DateTime.UtcNow)
                .Set(a => a.MarkedBy, "Admin")
                .Set(a => a.UpdatedAt, DateTime.UtcNow);
            if (_context.AdminAttendance.UpdateOne(filter, update).ModifiedCount == 0)
                throw new InvalidOperationException("This admin attendance record is no longer pending.");
            return true;
        }

        public void EnsureAdminAttendance(DateTime date)
        {
            if (!MeetingSchedule.IsWorkingDay(date))
                return;

            foreach (var admin in _context.Admins.Find(_ => true).ToList())
                GetAdminAttendance(admin.Id, date);
        }

        public List<AdminAttendanceReportRow> GetAllAdminAttendance(DateTime date)
        {
            if (!MeetingSchedule.IsWorkingDay(date))
                return new List<AdminAttendanceReportRow>();

            var admins = _context.Admins.Find(_ => true).ToList();
            return admins
                .SelectMany(admin => GetAdminAttendance(admin.Id, date).Select(record => new AdminAttendanceReportRow
                {
                    AdminId = admin.Id,
                    AdminName = string.IsNullOrWhiteSpace(admin.Name) ? admin.Username : admin.Name,
                    MeetingType = record.MeetingType,
                    Status = record.Status
                }))
                .OrderBy(row => row.AdminName)
                .ThenBy(row => Array.IndexOf(AdminAttendanceMeetingTypes, row.MeetingType))
                .ToList();
        }

        private void EnsureUserDailyAttendance(User user, DateTime date)
        {
            var dateText = FormatDate(date);
            var settings = GetMeetingSettings();
            var now = GetApplicationNow(settings);

            if (!User.IsSupportedPosition(user.Position))
                return;

            // Only ensure records for the current user, not all users
            foreach (var meetingType in MeetingSchedule.ForDate(settings, date, user.Position).Select(slot => slot.Type))
            {
                var filter = Builders<AttendanceRecord>.Filter.And(
                    Builders<AttendanceRecord>.Filter.Eq(a => a.UserId, user.Id),
                    Builders<AttendanceRecord>.Filter.Eq(a => a.MeetingDate, dateText),
                    Builders<AttendanceRecord>.Filter.Eq(a => a.MeetingType, meetingType));

                var record = new AttendanceRecord
                {
                    UserId = user.Id,
                    MeetingDate = dateText,
                    MeetingType = meetingType,
                    Team = user.Position
                };
                _context.Attendance.UpdateOne(filter, Builders<AttendanceRecord>.Update
                    .SetOnInsert(a => a.UserId, record.UserId)
                    .SetOnInsert(a => a.MeetingDate, record.MeetingDate)
                    .SetOnInsert(a => a.MeetingType, record.MeetingType)
                    .SetOnInsert(a => a.Team, record.Team)
                    .SetOnInsert(a => a.Status, record.Status)
                    .SetOnInsert(a => a.CreatedAt, record.CreatedAt)
                    .SetOnInsert(a => a.UpdatedAt, record.UpdatedAt),
                    new UpdateOptions { IsUpsert = true });
            }

            if (date.Date != now.Date)
                return;

            // Auto-mark absent if attendance window is closed
            foreach (var meetingType in MeetingSchedule.ForDate(settings, date, user.Position).Select(slot => slot.Type))
            {
                if (!IsWindowClosed(meetingType, settings, user.Position, now))
                    continue;

                var filter = Builders<AttendanceRecord>.Filter.And(
                    Builders<AttendanceRecord>.Filter.Eq(a => a.UserId, user.Id),
                    Builders<AttendanceRecord>.Filter.Eq(a => a.MeetingDate, dateText),
                    Builders<AttendanceRecord>.Filter.Eq(a => a.MeetingType, meetingType),
                    Builders<AttendanceRecord>.Filter.Eq(a => a.Status, AttendanceStatuses.Pending));
                _context.Attendance.UpdateOne(filter, Builders<AttendanceRecord>.Update
                    .Set(a => a.Status, AttendanceStatuses.Absent)
                    .Set(a => a.MarkedBy, "System")
                    .Set(a => a.UpdatedAt, DateTime.UtcNow));
            }
        }

        private void EnsureDailyAttendance(DateTime date)
        {
            var dateText = FormatDate(date);
            var settings = GetMeetingSettings();
            var now = GetApplicationNow(settings);
            var activeUsers = _context.Users.Find(u => u.IsActive).ToList();

            // Used by admin operations to ensure all users have attendance records
            var ensureOperations = activeUsers
                .Where(user => User.IsSupportedPosition(user.Position))
                .SelectMany(user => MeetingSchedule.ForDate(settings, date, user.Position).Select(slot =>
                {
                    var filter = Builders<AttendanceRecord>.Filter.And(
                        Builders<AttendanceRecord>.Filter.Eq(a => a.UserId, user.Id),
                        Builders<AttendanceRecord>.Filter.Eq(a => a.MeetingDate, dateText),
                        Builders<AttendanceRecord>.Filter.Eq(a => a.MeetingType, slot.Type));

                    var record = new AttendanceRecord
                    {
                        UserId = user.Id,
                        MeetingDate = dateText,
                        MeetingType = slot.Type,
                        Team = user.Position
                    };

                    return new UpdateOneModel<AttendanceRecord>(filter, Builders<AttendanceRecord>.Update
                        .SetOnInsert(a => a.UserId, record.UserId)
                        .SetOnInsert(a => a.MeetingDate, record.MeetingDate)
                        .SetOnInsert(a => a.MeetingType, record.MeetingType)
                        .SetOnInsert(a => a.Team, record.Team)
                        .SetOnInsert(a => a.Status, record.Status)
                        .SetOnInsert(a => a.CreatedAt, record.CreatedAt)
                        .SetOnInsert(a => a.UpdatedAt, record.UpdatedAt))
                    {
                        IsUpsert = true
                    };
                }))
                .ToList();

            if (ensureOperations.Count > 0)
            {
                _context.Attendance.BulkWrite(ensureOperations, new BulkWriteOptions { IsOrdered = false });
            }

            if (date.Date != now.Date)
                return;

            foreach (var team in User.SupportedPositions)
            {
                foreach (var meetingType in MeetingSchedule.ForDate(settings, date, team).Select(slot => slot.Type))
                {
                    if (!IsWindowClosed(meetingType, settings, team, now))
                        continue;

                    var teamUserIds = activeUsers
                        .Where(user => string.Equals(user.Position, team, StringComparison.Ordinal))
                        .Select(user => user.Id)
                        .ToList();
                    if (teamUserIds.Count == 0)
                        continue;

                    var filter = Builders<AttendanceRecord>.Filter.And(
                        Builders<AttendanceRecord>.Filter.In(a => a.UserId, teamUserIds),
                        Builders<AttendanceRecord>.Filter.Eq(a => a.MeetingDate, dateText),
                        Builders<AttendanceRecord>.Filter.Eq(a => a.MeetingType, meetingType),
                        Builders<AttendanceRecord>.Filter.Eq(a => a.Status, AttendanceStatuses.Pending));
                    _context.Attendance.UpdateMany(filter, Builders<AttendanceRecord>.Update
                        .Set(a => a.Status, AttendanceStatuses.Absent)
                        .Set(a => a.MarkedBy, "System")
                        .Set(a => a.UpdatedAt, DateTime.UtcNow));
                }
            }
        }

        private static string FormatDate(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static bool IsValidMeetingType(string meetingType) =>
            meetingType == MeetingTypes.Morning || meetingType == MeetingTypes.Weekly || meetingType == MeetingTypes.Evening;

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

        private static bool IsWithinAttendanceWindow(string meetingType, MeetingSettings settings, string position, DateTime now)
        {
            var start = GetMeetingStart(meetingType, settings, position, now.Date);
            return now >= start && now <= start.AddMinutes(15);
        }

        private static bool IsWindowClosed(string meetingType, MeetingSettings settings, string position, DateTime now)
        {
            return now > GetMeetingStart(meetingType, settings, position, now.Date).AddMinutes(15);
        }

        private static DateTime GetMeetingStart(string meetingType, MeetingSettings settings, string position, DateTime date)
        {
            var teamSettings = settings.GetTeamSettings(position);
            var time = meetingType switch
            {
                MeetingTypes.Morning => teamSettings.MorningTime,
                MeetingTypes.Weekly => settings.WeeklyTime,
                _ => teamSettings.EveningTime
            };
            if (!TimeSpan.TryParseExact(time, @"hh\:mm", CultureInfo.InvariantCulture, out var parsedTime))
                parsedTime = meetingType switch
                {
                    MeetingTypes.Morning => new TimeSpan(10, 0, 0),
                    MeetingTypes.Weekly => new TimeSpan(10, 0, 0),
                    _ => new TimeSpan(17, 0, 0)
                };
            return date.Add(parsedTime);
        }

        private void ValidateAdminId(string adminId)
        {
            if (string.IsNullOrWhiteSpace(adminId) || !_context.Admins.Find(a => a.Id == adminId).Any())
                throw new InvalidOperationException("The signed-in administrator could not be found.");
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

        public bool DeleteTaskComponent(string componentId)
        {
            if (string.IsNullOrWhiteSpace(componentId))
                return false;

            _context.ComponentAssignments.DeleteMany(assignment => assignment.ComponentId == componentId);
            return _context.Components.DeleteOne(component => component.Id == componentId).DeletedCount > 0;
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

        public List<DailyTaskUpdate> GetMyDailyHistory(string userId)
        {
            return _context.DailyTaskUpdates
                .Find(u => u.UserId == userId)
                .SortByDescending(u => u.UpdateDate)
                .ToList();
        }

        public List<DailyTaskUpdate> GetSelfStudyUpdates(string userId, bool isAdmin)
        {
            var filter = isAdmin
                ? Builders<DailyTaskUpdate>.Filter.Eq(u => u.UpdateType, DailyUpdateTypes.SelfStudy)
                : Builders<DailyTaskUpdate>.Filter.And(
                    Builders<DailyTaskUpdate>.Filter.Eq(u => u.UpdateType, DailyUpdateTypes.SelfStudy),
                    Builders<DailyTaskUpdate>.Filter.Eq(u => u.UserId, userId));

            return _context.DailyTaskUpdates.Find(filter).SortByDescending(u => u.UpdateDate).ToList();
        }

        public DailyTaskUpdate SaveDailyTaskUpdate(DailyTaskUpdate update)
        {
            update.UpdateType = string.IsNullOrWhiteSpace(update.UpdateType)
                ? DailyUpdateTypes.AssignedTask
                : update.UpdateType;

            if (string.IsNullOrWhiteSpace(update.UserId) || string.IsNullOrWhiteSpace(update.Description))
                throw new InvalidOperationException("A daily work description is required.");
            if (!new[] { TaskStatuses.NotStarted, TaskStatuses.InProgress, TaskStatuses.Blocked, TaskStatuses.Completed }.Contains(update.Status))
                throw new InvalidOperationException("The selected task status is invalid.");
            if (update.Status == TaskStatuses.Blocked && string.IsNullOrWhiteSpace(update.BlockedReason))
                throw new InvalidOperationException("A blocked reason is required.");

            var isSelfStudy = string.Equals(update.UpdateType, DailyUpdateTypes.SelfStudy, StringComparison.OrdinalIgnoreCase);
            if (isSelfStudy)
            {
                if (string.IsNullOrWhiteSpace(update.SelfStudyTopic))
                    throw new InvalidOperationException("A self study topic is required.");
                update.ComponentId = string.Empty;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(update.ComponentId))
                    throw new InvalidOperationException("A task component is required for task updates.");
                if (!_context.ComponentAssignments.Find(a => a.ComponentId == update.ComponentId && a.UserId == update.UserId && a.IsActive).Any())
                    throw new InvalidOperationException("This task is not assigned to your account.");
            }

            update.UpdateDate = DateTime.SpecifyKind(update.UpdateDate.Date, DateTimeKind.Unspecified);
            update.Description = update.Description.Trim();
            update.SelfStudyTopic = isSelfStudy ? update.SelfStudyTopic?.Trim() : null;
            update.BlockedReason = string.IsNullOrWhiteSpace(update.BlockedReason) ? null : update.BlockedReason.Trim();
            update.UpdatedAt = DateTime.UtcNow;

            var existingToday = _context.DailyTaskUpdates
                .Find(u => u.UserId == update.UserId && u.UpdateDate == update.UpdateDate)
                .FirstOrDefault();

            if (existingToday != null)
            {
                throw new InvalidOperationException("You already submitted one daily update today. You can't submit another update on the same day.");
            }

            var filter = Builders<DailyTaskUpdate>.Filter.And(
                Builders<DailyTaskUpdate>.Filter.Eq(u => u.ComponentId, update.ComponentId),
                Builders<DailyTaskUpdate>.Filter.Eq(u => u.UserId, update.UserId),
                Builders<DailyTaskUpdate>.Filter.Eq(u => u.UpdateDate, update.UpdateDate));
            _context.DailyTaskUpdates.ReplaceOne(filter, update, new ReplaceOptions { IsUpsert = true });
            return update;
        }

        public AdminDailyTaskUpdate? GetAdminDailyTask(string adminId, DateTime date)
        {
            ValidateAdminId(adminId);
            var calendarDate = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
            return _context.AdminDailyTaskUpdates.Find(u => u.AdminId == adminId && u.UpdateDate == calendarDate).FirstOrDefault();
        }

        public AdminDailyTaskUpdate SaveAdminDailyTask(AdminDailyTaskUpdate update)
        {
            ValidateAdminId(update.AdminId);
            if (string.IsNullOrWhiteSpace(update.Description))
                throw new InvalidOperationException("A daily work description is required.");
            if (!new[] { TaskStatuses.NotStarted, TaskStatuses.InProgress, TaskStatuses.Blocked, TaskStatuses.Completed }.Contains(update.Status))
                throw new InvalidOperationException("The selected task status is invalid.");
            if (update.Status == TaskStatuses.Blocked && string.IsNullOrWhiteSpace(update.BlockedReason))
                throw new InvalidOperationException("A blocked reason is required.");

            update.UpdateDate = DateTime.SpecifyKind(update.UpdateDate.Date, DateTimeKind.Unspecified);
            if (GetAdminDailyTask(update.AdminId, update.UpdateDate) != null)
                throw new InvalidOperationException("You already submitted an admin daily task for this date.");

            update.Description = update.Description.Trim();
            update.BlockedReason = string.IsNullOrWhiteSpace(update.BlockedReason) ? null : update.BlockedReason.Trim();
            update.UpdatedAt = DateTime.UtcNow;
            _context.AdminDailyTaskUpdates.InsertOne(update);
            return update;
        }

        public List<AdminDailyTaskReportRow> GetAllAdminDailyTasks(DateTime date)
        {
            var calendarDate = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
            var tasks = _context.AdminDailyTaskUpdates.Find(u => u.UpdateDate == calendarDate)
                .ToList()
                .ToDictionary(task => task.AdminId, StringComparer.Ordinal);

            return _context.Admins.Find(_ => true).ToList()
                .Select(admin =>
                {
                    tasks.TryGetValue(admin.Id, out var task);
                    return new AdminDailyTaskReportRow
                    {
                        AdminId = admin.Id,
                        AdminName = string.IsNullOrWhiteSpace(admin.Name) ? admin.Username : admin.Name,
                        Status = task?.Status ?? "Not submitted",
                        Description = task?.Description ?? "No daily task submitted.",
                        BlockedReason = task?.BlockedReason
                    };
                })
                .OrderBy(row => row.AdminName)
                .ToList();
        }

        public List<ProjectDailyTaskReportRow> GetProjectDailyTaskReport(string projectId, DateTime date)
        {
            var calendarDate = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
            var project = _context.Projects.Find(p => p.Id == projectId).FirstOrDefault()
                ?? throw new InvalidOperationException("The project could not be found.");
            var components = _context.Components.Find(c => c.ProjectId == projectId).ToList();
            var users = _context.Users.Find(_ => true).ToList().ToDictionary(u => u.Id);
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
                        UserName = users.TryGetValue(assignment.UserId, out var user) ? user.Username ?? user.Email : "Unknown member",
                        Position = users.TryGetValue(assignment.UserId, out user) ? user.Position : string.Empty,
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
