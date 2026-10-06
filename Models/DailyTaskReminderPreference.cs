using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DMS.Models;

public sealed class DailyTaskReminderPreference
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    public string RecipientId { get; set; } = string.Empty;
    public string RecipientRole { get; set; } = string.Empty;
    public string StoppedDate { get; set; } = string.Empty;
}
