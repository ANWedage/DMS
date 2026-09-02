using System.ComponentModel;
using System.Runtime.CompilerServices;
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
