using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DMS.Models;

public static class ProjectStatuses
{
    public const string Draft = "Draft";
    public const string Active = "Active";
    public const string Completed = "Completed";
    public const string Archived = "Archived";
}

public static class TaskStatuses
{
    public const string NotStarted = "Not started";
    public const string InProgress = "In progress";
    public const string Blocked = "Blocked";
    public const string Completed = "Completed";
}

public static class TaskPriorities
{
    public const string Low = "Low";
    public const string Medium = "Medium";
    public const string High = "High";
}

public class TaskProject
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    [BsonDateTimeOptions(Kind = DateTimeKind.Unspecified)]
    public DateTime StartDate { get; set; } = DateTime.Today;
    [BsonDateTimeOptions(Kind = DateTimeKind.Unspecified)]
    public DateTime DueDate { get; set; } = DateTime.Today.AddDays(30);
    public string Status { get; set; } = ProjectStatuses.Draft;
    public string CreatedByAdminId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class TaskComponent
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string ProjectId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Priority { get; set; } = TaskPriorities.Medium;
    [BsonDateTimeOptions(Kind = DateTimeKind.Unspecified)]
    public DateTime DueDate { get; set; } = DateTime.Today.AddDays(7);
    public string Status { get; set; } = TaskStatuses.NotStarted;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class ComponentAssignment
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string ComponentId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string AssignedByAdminId { get; set; } = string.Empty;
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}

public class DailyTaskUpdate
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    public string ComponentId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    [BsonDateTimeOptions(Kind = DateTimeKind.Unspecified)]
    public DateTime UpdateDate { get; set; } = DateTime.Today;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = TaskStatuses.InProgress;
    public string? BlockedReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AssignedTask
{
    public TaskProject Project { get; init; } = new();
    public TaskComponent Component { get; init; } = new();
    public DailyTaskUpdate? LatestUpdate { get; init; }
}

public sealed class ProjectDailyTaskReportRow
{
    public string ProjectId { get; init; } = string.Empty;
    public string ProjectName { get; init; } = string.Empty;
    public string ComponentId { get; init; } = string.Empty;
    public string ComponentName { get; init; } = string.Empty;
    public string ComponentDescription { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string UserName { get; init; } = string.Empty;
    public string Status { get; init; } = "Not submitted";
    public string DailyWork { get; init; } = "No update submitted";
    [BsonDateTimeOptions(Kind = DateTimeKind.Unspecified)]
    public DateTime UpdateDate { get; init; }
    public bool HasSubmittedUpdate { get; init; }
}