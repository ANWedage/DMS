using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DMS.Models
{
    public sealed class Notification
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        [BsonElement("RecipientId")]
        public string RecipientId { get; set; } = string.Empty;

        [BsonElement("RecipientRole")]
        public string RecipientRole { get; set; } = string.Empty;

        [BsonElement("SenderId")]
        public string SenderId { get; set; } = string.Empty;

        [BsonElement("SenderName")]
        public string SenderName { get; set; } = string.Empty;

        [BsonElement("Title")]
        public string Title { get; set; } = string.Empty;

        [BsonElement("Message")]
        public string Message { get; set; } = string.Empty;

        [BsonElement("CreatedAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("IsRead")]
        public bool IsRead { get; set; }

        [BsonElement("ReadAt")]
        public DateTime? ReadAt { get; set; }
    }

    public sealed record NotificationRecipient(string Id, string DisplayName, string Role);
}