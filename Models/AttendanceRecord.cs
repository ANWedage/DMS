using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DMS.Models
{
    public static class AttendanceStatuses
    {
        public const string Pending = "Pending";
        public const string Present = "Present";
        public const string Absent = "Absent";
        public const string AbsentInformed = "Absent (informed)";
    }

    public static class MeetingTypes
    {
        public const string Morning = "Morning";
        public const string Evening = "Evening";
    }

    public class AttendanceRecord
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        [BsonElement("UserId")]
        public string UserId { get; set; } = string.Empty;

        [BsonElement("MeetingDate")]
        public string MeetingDate { get; set; } = string.Empty;

        [BsonElement("MeetingType")]
        public string MeetingType { get; set; } = string.Empty;

        [BsonElement("Status")]
        public string Status { get; set; } = AttendanceStatuses.Pending;

        [BsonElement("MarkedAt")]
        [BsonIgnoreIfNull]
        public DateTime? MarkedAt { get; set; }

        [BsonElement("MarkedBy")]
        [BsonIgnoreIfNull]
        public string? MarkedBy { get; set; }

        [BsonElement("ChangedByAdminId")]
        [BsonIgnoreIfNull]
        public string? ChangedByAdminId { get; set; }

        [BsonElement("ChangedByAdminName")]
        [BsonIgnoreIfNull]
        public string? ChangedByAdminName { get; set; }

        [BsonElement("AdminNote")]
        [BsonIgnoreIfNull]
        public string? AdminNote { get; set; }

        [BsonElement("CreatedAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("UpdatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
