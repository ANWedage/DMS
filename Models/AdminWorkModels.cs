using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DMS.Models;

public sealed class AdminAttendanceRecord
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string AdminId { get; set; } = string.Empty;
    public string MeetingDate { get; set; } = string.Empty;
    public string MeetingType { get; set; } = string.Empty;
    public string Status { get; set; } = AttendanceStatuses.Pending;
    public DateTime? MarkedAt { get; set; }
    public string? MarkedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AdminDailyTaskUpdate
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string AdminId { get; set; } = string.Empty;
    [BsonDateTimeOptions(Kind = DateTimeKind.Unspecified)]
    public DateTime UpdateDate { get; set; } = DateTime.Today;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = TaskStatuses.InProgress;
    public string? BlockedReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AdminAttendanceReportRow
{
    public string AdminId { get; init; } = string.Empty;
    public string AdminName { get; init; } = string.Empty;
    public string MeetingType { get; init; } = string.Empty;
    public string Status { get; init; } = AttendanceStatuses.Pending;
}

public sealed class AdminDailyTaskReportRow
{
    public string AdminId { get; init; } = string.Empty;
    public string AdminName { get; init; } = string.Empty;
    public string Status { get; init; } = "Not submitted";
    public string Description { get; init; } = "No daily task submitted.";
    public string? BlockedReason { get; init; }
}