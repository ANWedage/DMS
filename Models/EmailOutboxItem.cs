using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DMS.Models;

public sealed class EmailOutboxItem
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonElement("DeduplicationKey")]
    public string DeduplicationKey { get; set; } = string.Empty;

    [BsonElement("RecipientUserId")]
    public string RecipientUserId { get; set; } = string.Empty;

    [BsonElement("RecipientEmail")]
    public string RecipientEmail { get; set; } = string.Empty;

    [BsonElement("RecipientName")]
    public string RecipientName { get; set; } = string.Empty;

    [BsonElement("SenderName")]
    public string SenderName { get; set; } = string.Empty;

    [BsonElement("AlertType")]
    public string AlertType { get; set; } = string.Empty;

    [BsonElement("Status")]
    public string Status { get; set; } = "Pending";

    [BsonElement("Attempts")]
    public int Attempts { get; set; }

    [BsonElement("NextAttemptAt")]
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;

    [BsonElement("ProcessingStartedAt")]
    [BsonIgnoreIfNull]
    public DateTime? ProcessingStartedAt { get; set; }

    [BsonElement("LastError")]
    [BsonIgnoreIfNull]
    public string? LastError { get; set; }

    [BsonElement("CreatedAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("SentAt")]
    [BsonIgnoreIfNull]
    public DateTime? SentAt { get; set; }
}
