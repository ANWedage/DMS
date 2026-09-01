using System;
using DMS.Helpers;
using DMS.Models;

namespace DMS.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly User _currentUser;

        public string WelcomeText => $"Logged in as {_currentUser.Username}";
        public string Username => _currentUser.Username ?? "Workspace member";
        public string Email => _currentUser.Email;
        public string Initials => string.IsNullOrWhiteSpace(_currentUser.Username)
            ? "GM"
            : _currentUser.Username[..1].ToUpperInvariant();

        public RelayCommand LogoutCommand { get; }

        /// <summary>Raised when the user clicks Logout (top-right corner).</summary>
        public Action? LogoutRequested { get; set; }

        public MainViewModel(User currentUser)
        {
            _currentUser = currentUser;
            LogoutCommand = new RelayCommand(_ => LogoutRequested?.Invoke());
        }

        // Sprint 2+: project list, task board, dashboard etc. go here.
    }
}
