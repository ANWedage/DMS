using DMS.Models;

namespace DMS.Services
{
    public interface IUserService
    {
        /// <summary>Creates an account with email, contact number, and password. Username is not set yet.</summary>
        /// <exception cref="InvalidOperationException">Thrown if the email is already registered.</exception>
        User CreateAccount(string email, string contactNumber, string password);

        /// <summary>Sets the username for a freshly created account (post-signup popup step).</summary>
        /// <exception cref="InvalidOperationException">Thrown if the username is already taken.</exception>
        void SetUsername(string userId, string username);

        /// <summary>Validates username + password. Returns the user on success, null otherwise.</summary>
        User? Login(string username, string password);

        bool EmailExists(string email);
        bool UsernameExists(string username);
    }
}
