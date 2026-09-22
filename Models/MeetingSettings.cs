using MongoDB.Bson.Serialization.Attributes;

namespace DMS.Models
{
    public class TeamMeetingSettings
    {
        [BsonElement("MorningTime")]
        public string MorningTime { get; set; } = "10:00";

        [BsonElement("EveningTime")]
        public string EveningTime { get; set; } = "17:00";

        [BsonElement("MorningMeetingLink")]
        public string MorningMeetingLink { get; set; } = string.Empty;

        [BsonElement("EveningMeetingLink")]
        public string EveningMeetingLink { get; set; } = string.Empty;

        public TeamMeetingSettings Clone() => new()
        {
            MorningTime = MorningTime,
            EveningTime = EveningTime,
            MorningMeetingLink = MorningMeetingLink,
            EveningMeetingLink = EveningMeetingLink
        };
    }

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

        [BsonElement("FullStack")]
        public TeamMeetingSettings? FullStack { get; set; }

        [BsonElement("QA")]
        public TeamMeetingSettings? QA { get; set; }

        [BsonElement("UIUX")]
        public TeamMeetingSettings? UIUX { get; set; }

        [BsonElement("WeeklyTime")]
        public string WeeklyTime { get; set; } = "10:00";

        [BsonElement("WeeklyMeetingLink")]
        public string WeeklyMeetingLink { get; set; } = string.Empty;

        [BsonElement("DailyTaskFormLink")]
        public string DailyTaskFormLink { get; set; } = string.Empty;

        [BsonElement("LeaveFormLink")]
        public string LeaveFormLink { get; set; } = string.Empty;

        [BsonElement("TimeZoneId")]
        public string TimeZoneId { get; set; } = "Sri Lanka Standard Time";

        [BsonElement("UpdatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("UpdatedByAdminId")]
        public string? UpdatedByAdminId { get; set; }

        [BsonElement("UpdatedByAdminName")]
        public string? UpdatedByAdminName { get; set; }

        public TeamMeetingSettings GetTeamSettings(string? position)
        {
            return position switch
            {
                "Full Stack" => FullStack?.Clone() ?? LegacyTeamSettings(),
                "QA" => QA?.Clone() ?? LegacyTeamSettings(),
                "UI/UX" => UIUX?.Clone() ?? LegacyTeamSettings(),
                _ => new TeamMeetingSettings()
            };
        }

        public void EnsureTeamSettings()
        {
            var legacy = LegacyTeamSettings();
            FullStack ??= legacy.Clone();
            QA ??= legacy.Clone();
            UIUX ??= legacy.Clone();
        }

        private TeamMeetingSettings LegacyTeamSettings() => new()
        {
            MorningTime = MorningTime,
            EveningTime = EveningTime,
            MorningMeetingLink = MorningMeetingLink,
            EveningMeetingLink = EveningMeetingLink
        };
    }
}
