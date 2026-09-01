using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DMS.Models
{
    public class User
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        [BsonElement("Email")]
        public string Email { get; set; } = string.Empty;

        [BsonElement("ContactNumber")]
        public string ContactNumber { get; set; } = string.Empty;

        [BsonElement("PasswordHash")]
        public string PasswordHash { get; set; } = string.Empty;

        [BsonElement("PasswordSalt")]
        public string PasswordSalt { get; set; } = string.Empty;

        // Null until the user completes the "create username" step
        [BsonElement("Username")]
        [BsonIgnoreIfNull]
        public string? Username { get; set; }
    }
}
