using System;
using System.Windows;
using DMS.Helpers;
using DMS.Models;
using DMS.Services;
using DMS.Views;

namespace DMS.ViewModels
{
    public class RegisterViewModel : ViewModelBase
    {
        private readonly IUserService _userService;

        private string _email = string.Empty;
        public string Email
        {
            get => _email;
            set => SetProperty(ref _email, value);
        }

        private string _contactNumber = string.Empty;
        public string ContactNumber
        {
            get => _contactNumber;
            set => SetProperty(ref _contactNumber, value);
        }

        // PasswordBox can't be data-bound safely, so the code-behind pushes
        // the values in here before executing the command.
        public string Password { get; set; } = string.Empty;
        public string ConfirmPassword { get; set; } = string.Empty;

        private string _errorMessage = string.Empty;
        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public RelayCommand CreateAccountCommand { get; }

        public Action? CloseAction { get; set; }

        public RegisterViewModel(IUserService userService)
        {
            _userService = userService;
            CreateAccountCommand = new RelayCommand(_ => CreateAccount());
        }

        private void CreateAccount()
        {
            ErrorMessage = string.Empty;

            var email = Email.Trim();
            var contactNumber = ContactNumber.Trim();

            if (string.IsNullOrWhiteSpace(email) || !SecurityValidator.IsValidEmail(email))
            {
                ErrorMessage = "Enter a valid email address.";
                return;
            }

            if (string.IsNullOrWhiteSpace(contactNumber))
            {
                ErrorMessage = "Enter a contact number.";
                return;
            }

            if (!SecurityValidator.IsStrongPassword(Password))
            {
                ErrorMessage = "Password must be at least 8 characters and include uppercase, lowercase, a number, and a symbol.";
                return;
            }

            if (Password != ConfirmPassword)
            {
                ErrorMessage = "Passwords do not match.";
                return;
            }

            try
            {
                // Immediately prompt for a username, per the required first-sprint flow.
                var usernameViewModel = new UsernameViewModel(_userService);
                var usernameWindow = new UsernameWindow(usernameViewModel);
                usernameWindow.ShowDialog();

                if (string.IsNullOrWhiteSpace(usernameViewModel.ConfirmedUsername))
                    return;

                _userService.CreateAccount(email, contactNumber, Password, usernameViewModel.ConfirmedUsername);

                CloseAction?.Invoke();
            }
            catch (InvalidOperationException ex)
            {
                ErrorMessage = ex.Message;
            }
            catch (Exception)
            {
                ErrorMessage = "We could not create the account because the account service is unavailable.";
            }
        }
    }
}
