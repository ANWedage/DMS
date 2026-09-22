using MongoDB.Driver;
using MongoDB.Driver.GridFS;
using DMS.Models;

namespace DMS.Data
{
    public class MongoDbContext
    {
        private readonly IMongoDatabase _database;

        public MongoDbContext()
        {
            var client = new MongoClient(MongoConfig.ConnectionString);
            _database = client.GetDatabase(MongoConfig.DatabaseName);
        }

        public IMongoCollection<User> Users => _database.GetCollection<User>("Users");
        public IMongoCollection<AdminUser> Admins => _database.GetCollection<AdminUser>("Admins");
        public IMongoCollection<AttendanceRecord> Attendance => _database.GetCollection<AttendanceRecord>("Attendance");
        public IMongoCollection<AdminAttendanceRecord> AdminAttendance => _database.GetCollection<AdminAttendanceRecord>("AdminAttendance");
        public IMongoCollection<MeetingSettings> MeetingSettings => _database.GetCollection<MeetingSettings>("MeetingSettings");
        public IMongoCollection<TaskProject> Projects => _database.GetCollection<TaskProject>("Projects");
        public IMongoCollection<TaskComponent> Components => _database.GetCollection<TaskComponent>("ProjectComponents");
        public IMongoCollection<ComponentAssignment> ComponentAssignments => _database.GetCollection<ComponentAssignment>("ComponentAssignments");
        public IMongoCollection<DailyTaskUpdate> DailyTaskUpdates => _database.GetCollection<DailyTaskUpdate>("DailyTaskUpdates");
        public IMongoCollection<AdminDailyTaskUpdate> AdminDailyTaskUpdates => _database.GetCollection<AdminDailyTaskUpdate>("AdminDailyTaskUpdates");
        public IMongoCollection<Notification> Notifications => _database.GetCollection<Notification>("Notifications");
        public IMongoCollection<ChatMessage> ChatMessages => _database.GetCollection<ChatMessage>("ChatMessages");
        public IMongoCollection<ChatAttachment> ChatAttachments => _database.GetCollection<ChatAttachment>("ChatAttachments");
        public GridFSBucket ChatAttachmentsBucket => new(_database, new GridFSBucketOptions
        {
            BucketName = "ChatAttachmentsBucket",
            ChunkSizeBytes = 255 * 1024,
            WriteConcern = WriteConcern.WMajority,
            ReadConcern = ReadConcern.Majority
        });

        /// <summary>Creates unique indexes on Email and Username (first run only - safe to call every startup).</summary>
        public void EnsureIndexes()
        {
            var emailIndex = new CreateIndexModel<User>(
                Builders<User>.IndexKeys.Ascending(u => u.Email),
                new CreateIndexOptions { Unique = true });

            var usernameIndex = new CreateIndexModel<User>(
                Builders<User>.IndexKeys.Ascending(u => u.Username),
                new CreateIndexOptions { Unique = true, Sparse = true });

            var adminUsernameIndex = new CreateIndexModel<AdminUser>(
                Builders<AdminUser>.IndexKeys.Ascending(a => a.Username),
                new CreateIndexOptions { Unique = true });

            Users.Indexes.CreateMany(new[] { emailIndex, usernameIndex });
            Admins.Indexes.CreateOne(adminUsernameIndex);

            var attendanceIndex = new CreateIndexModel<AttendanceRecord>(
                Builders<AttendanceRecord>.IndexKeys
                    .Ascending(a => a.UserId)
                    .Ascending(a => a.MeetingDate)
                    .Ascending(a => a.MeetingType),
                new CreateIndexOptions { Unique = true });

            Attendance.Indexes.CreateOne(attendanceIndex);
            Attendance.Indexes.CreateOne(new CreateIndexModel<AttendanceRecord>(
                Builders<AttendanceRecord>.IndexKeys
                    .Ascending(a => a.MeetingDate)
                    .Ascending(a => a.UserId)
                    .Ascending(a => a.MeetingType)));

            AdminAttendance.Indexes.CreateOne(new CreateIndexModel<AdminAttendanceRecord>(
                Builders<AdminAttendanceRecord>.IndexKeys
                    .Ascending(a => a.AdminId)
                    .Ascending(a => a.MeetingDate)
                    .Ascending(a => a.MeetingType),
                new CreateIndexOptions { Unique = true }));

            ComponentAssignments.Indexes.CreateOne(new CreateIndexModel<ComponentAssignment>(
                Builders<ComponentAssignment>.IndexKeys.Ascending(a => a.ComponentId).Ascending(a => a.UserId),
                new CreateIndexOptions { Unique = true }));
            DailyTaskUpdates.Indexes.CreateOne(new CreateIndexModel<DailyTaskUpdate>(
                Builders<DailyTaskUpdate>.IndexKeys.Ascending(u => u.ComponentId).Ascending(u => u.UserId).Ascending(u => u.UpdateDate),
                new CreateIndexOptions { Unique = true }));
            AdminDailyTaskUpdates.Indexes.CreateOne(new CreateIndexModel<AdminDailyTaskUpdate>(
                Builders<AdminDailyTaskUpdate>.IndexKeys.Ascending(u => u.AdminId).Ascending(u => u.UpdateDate),
                new CreateIndexOptions { Unique = true }));

            Notifications.Indexes.CreateOne(new CreateIndexModel<Notification>(
                Builders<Notification>.IndexKeys.Ascending(n => n.RecipientId).Ascending(n => n.RecipientRole).Ascending(n => n.IsRead).Descending(n => n.CreatedAt)));
            Notifications.Indexes.CreateOne(new CreateIndexModel<Notification>(
                Builders<Notification>.IndexKeys.Ascending(n => n.RecipientId).Ascending(n => n.ReminderKey),
                new CreateIndexOptions<Notification>
                {
                    Name = "Notifications_ReminderKey_Unique",
                    Unique = true,
                    PartialFilterExpression = Builders<Notification>.Filter.Exists(n => n.ReminderKey, true)
                }));

            ChatMessages.Indexes.CreateMany(new[]
            {
                new CreateIndexModel<ChatMessage>(
                    Builders<ChatMessage>.IndexKeys.Ascending(m => m.ConversationKey).Ascending(m => m.CreatedAt)),
                new CreateIndexModel<ChatMessage>(
                    Builders<ChatMessage>.IndexKeys.Ascending(m => m.RecipientId).Ascending(m => m.RecipientRole).Ascending(m => m.ReadAt)),
                new CreateIndexModel<ChatMessage>(
                    Builders<ChatMessage>.IndexKeys.Ascending(m => m.AttachmentId))
            });

            ChatAttachments.Indexes.CreateMany(new[]
            {
                new CreateIndexModel<ChatAttachment>(
                    Builders<ChatAttachment>.IndexKeys.Ascending(a => a.ConversationKey).Ascending(a => a.CreatedAt)),
                new CreateIndexModel<ChatAttachment>(
                    Builders<ChatAttachment>.IndexKeys.Ascending(a => a.RecipientId).Ascending(a => a.RecipientRole).Ascending(a => a.IsDeleted))
            });
        }
    }
}
