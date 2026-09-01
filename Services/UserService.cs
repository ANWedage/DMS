using MongoDB.Driver;
using DMS.Data;
using DMS.Helpers;
using DMS.Models;

namespace DMS.Services
{
    public class UserService : IUserService
    {
        private readonly MongoDbContext _context;

        public UserService(MongoDbContext context)
        {
            _context = context;
        }

        public User CreateAccount(string email, string contactNumber, string password)
        {
            var trimmedEmail = SecurityValidator.NormalizeEmail(email);
            var trimmedContactNumber = contactNumber.Trim();
            var trimmedPassword = password ?? string.Empty;

            if (!SecurityValidator.IsValidEmail(trimmedEmail))
                throw new InvalidOperationException("Enter a valid email address.");

            if (!SecurityValidator.IsStrongPassword(trimmedPassword))
                throw new InvalidOperationException("Password must be at least 8 characters and include uppercase, lowercase, a number, and a symbol.");

            if (EmailExists(trimmedEmail))
                throw new InvalidOperationException("An account with this email already exists.");

            var (hash, salt) = PasswordHasher.HashPassword(trimmedPassword);

            var user = new User
            {
                Email = trimmedEmail,
                ContactNumber = trimmedContactNumber,
                PasswordHash = hash,
                PasswordSalt = salt,
                Username = null
            };

            _context.Users.InsertOne(user);
            return user;
        }

        public void SetUsername(string userId, string username)
        {
            var normalizedUsername = SecurityValidator.NormalizeUsername(username);
            if (!SecurityValidator.IsValidUsername(normalizedUsername))
                throw new InvalidOperationException("Username must be 3-20 characters, letters/numbers/underscore only, and contain no spaces.");

            if (UsernameExists(normalizedUsername))
                throw new InvalidOperationException("That username is already taken.");

            var filter = Builders<User>.Filter.Eq(u => u.Id, userId);
            var update = Builders<User>.Update.Set(u => u.Username, normalizedUsername);
            _context.Users.UpdateOne(filter, update);
        }

        public User? Login(string username, string password)
        {
            var normalizedUsername = SecurityValidator.NormalizeUsername(username);
            var user = _context.Users.Find(u => u.Username == normalizedUsername).FirstOrDefault();
            if (user is null) return null;

            return PasswordHasher.Verify(password ?? string.Empty, user.PasswordHash, user.PasswordSalt)
                ? user
                : null;
        }

        public AdminUser? LoginAdmin(string username, string password)
        {
            var normalizedUsername = SecurityValidator.NormalizeUsername(username);
            var admin = _context.Admins.Find(a => a.Username == normalizedUsername).FirstOrDefault();
            if (admin is null) return null;

            return PasswordHasher.Verify(password ?? string.Empty, admin.PasswordHash, admin.PasswordSalt)
                ? admin
                : null;
        }

        public User GetUserById(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new InvalidOperationException("User session is missing.");

            var user = _context.Users.Find(u => u.Id == userId).FirstOrDefault();
            if (user == null)
                throw new InvalidOperationException("This user account could not be found.");

            return user;
        }

        public List<User> GetAllUsers()
        {
            var users = _context.Users.Find(_ => true).ToList();
            return users
                .OrderBy(u => string.IsNullOrWhiteSpace(u.Username) ? u.Email : u.Username)
                .ToList();
        }

        public bool CanAccessUser(string targetUserId)
        {
            var activeUserId = AppSession.CurrentUserId;
            if (string.IsNullOrWhiteSpace(activeUserId) || string.IsNullOrWhiteSpace(targetUserId))
                return false;

            return string.Equals(activeUserId, targetUserId, StringComparison.Ordinal);
        }

        public bool EmailExists(string email)
        {
            var normalizedEmail = SecurityValidator.NormalizeEmail(email);
            return _context.Users.Find(u => u.Email == normalizedEmail).Any();
        }

        public bool UsernameExists(string username)
        {
            var normalizedUsername = SecurityValidator.NormalizeUsername(username);
            return _context.Users.Find(u => u.Username == normalizedUsername).Any();
        }
    }
}
