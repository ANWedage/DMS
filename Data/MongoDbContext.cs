using MongoDB.Driver;
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
        public IMongoCollection<MeetingSettings> MeetingSettings => _database.GetCollection<MeetingSettings>("MeetingSettings");

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
        }
    }
}
