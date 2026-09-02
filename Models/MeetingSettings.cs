using MongoDB.Bson.Serialization.Attributes;

namespace DMS.Models
{
    public class MeetingSettings
    {
        public const string DefaultId = "default";

        [BsonId]
        public string Id { get; set; } = DefaultId;

        [BsonElement("MorningTime")]
        public string MorningTime { get; set; } = "10:00";

        [BsonElement("EveningTime")]
        public string EveningTime { get; set; } = "17:00";

        [BsonElement("MorningMeetingLink")]
        public string MorningMeetingLink { get; set; } = string.Empty;

        [BsonElement("EveningMeetingLink")]
        public string EveningMeetingLink { get; set; } = string.Empty;

        [BsonElement("TimeZoneId")]
        public string TimeZoneId { get; set; } = "Sri Lanka Standard Time";

        [BsonElement("UpdatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("UpdatedByAdminId")]
        public string? UpdatedByAdminId { get; set; }

        [BsonElement("UpdatedByAdminName")]
        public string? UpdatedByAdminName { get; set; }
    }
}
