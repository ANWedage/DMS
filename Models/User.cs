using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DMS.Models
{
    public class User : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        [BsonElement("Email")]
        public string Email { get; set; } = string.Empty;

        [BsonElement("ContactNumber")]
        public string ContactNumber { get; set; } = string.Empty;

        private string _position = string.Empty;
        [BsonElement("Position")]
        public string Position
        {
            get => _position;
            set
            {
                var normalized = NormalizePosition(value);
                if (_position == normalized) return;
                _position = normalized;
                OnPropertyChanged();
            }
        }

        public static string[] SupportedPositions => new[] { "QA", "UI/UX", "Full Stack" };

        public static string NormalizePosition(string? position)
        {
            if (string.IsNullOrWhiteSpace(position))
                return string.Empty;

            var trimmed = position.Trim();
            return trimmed switch
            {
                "QA" => "QA",
                "UI/UX" => "UI/UX",
                "Full Stack" => "Full Stack",
                _ => string.Empty
            };
        }

        public static bool IsSupportedPosition(string? position) => NormalizePosition(position) != string.Empty;

        [BsonElement("PasswordHash")]
        public string PasswordHash { get; set; } = string.Empty;

        [BsonElement("PasswordSalt")]
        public string PasswordSalt { get; set; } = string.Empty;

        private bool _isActive = true;
        [BsonElement("IsActive")]
        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive == value) return;
                _isActive = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Status));
            }
        }

        [BsonIgnore]
        public string Status => IsActive ? "Active" : "Inactive";

        [BsonIgnore]
        [JsonIgnore]
        public bool HasSubmittedDailyTask { get; set; }

        [BsonIgnore]
        [JsonIgnore]
        public string DailyTaskSubmissionStatus => HasSubmittedDailyTask ? "Submitted" : "Not submitted";

        private string? _deactivatedByAdminName;
        [BsonElement("DeactivatedByAdminName")]
        [BsonIgnoreIfNull]
        public string? DeactivatedByAdminName
        {
            get => _deactivatedByAdminName;
            set
            {
                if (_deactivatedByAdminName == value) return;
                _deactivatedByAdminName = value;
                OnPropertyChanged();
            }
        }

        // Null until the user completes the "create username" step
        [BsonElement("Username")]
        [BsonIgnoreIfNull]
        public string? Username { get; set; }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
