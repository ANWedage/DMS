using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DMS.Models;

public sealed record ChatUser(string Id, string DisplayName, string Role, bool IsActive = true, bool IsOnline = false);

public sealed class ChatMessage
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonElement("ConversationKey")]
    public string ConversationKey { get; set; } = string.Empty;

    [BsonElement("SenderId")]
    public string SenderId { get; set; } = string.Empty;

    [BsonElement("SenderRole")]
    public string SenderRole { get; set; } = string.Empty;

    [BsonElement("RecipientId")]
    public string RecipientId { get; set; } = string.Empty;

    [BsonElement("RecipientRole")]
    public string RecipientRole { get; set; } = string.Empty;

    [BsonElement("MessageText")]
    public string MessageText { get; set; } = string.Empty;

    [BsonElement("CreatedAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("ReadAt")]
    [BsonIgnoreIfNull]
    public DateTime? ReadAt { get; set; }
}

public sealed record ChatConversationSummary(
    string OtherUserId,
    string OtherDisplayName,
    string OtherRole,
    string LatestMessage,
    DateTime LatestMessageAt,
    int UnreadCount);