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

        /// <summary>Creates unique indexes on Email and Username (first run only - safe to call every startup).</summary>
        public void EnsureIndexes()
        {
            var emailIndex = new CreateIndexModel<User>(
                Builders<User>.IndexKeys.Ascending(u => u.Email),
                new CreateIndexOptions { Unique = true });

            var usernameIndex = new CreateIndexModel<User>(
                Builders<User>.IndexKeys.Ascending(u => u.Username),
                new CreateIndexOptions { Unique = true, Sparse = true });

            Users.Indexes.CreateMany(new[] { emailIndex, usernameIndex });
        }
    }
}
