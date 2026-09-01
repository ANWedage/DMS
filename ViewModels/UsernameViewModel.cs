using System;
using DMS.Helpers;
using DMS.Services;

namespace DMS.ViewModels
{
    public class UsernameViewModel : ViewModelBase
    {
        private readonly IUserService _userService;
        private readonly string _userId;

        private string _username = string.Empty;
        public string Username
        {
            get => _username;
            set => SetProperty(ref _username, value);
        }

        private string _errorMessage = string.Empty;
        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public RelayCommand ConfirmCommand { get; }

        public Action? CloseAction { get; set; }

        public UsernameViewModel(IUserService userService, string userId)
        {
            _userService = userService;
            _userId = userId;
            ConfirmCommand = new RelayCommand(_ => Confirm());
        }

        private void Confirm()
        {
            ErrorMessage = string.Empty;

            var normalizedUsername = Username.Trim();
            if (!SecurityValidator.IsValidUsername(normalizedUsername))
            {
                ErrorMessage = "Username must be 3-20 characters, letters/numbers/underscore only, and contain no spaces.";
                return;
            }

            try
            {
                _userService.SetUsername(_userId, normalizedUsername);
                CloseAction?.Invoke();
            }
            catch (InvalidOperationException ex)
            {
                ErrorMessage = ex.Message;
            }
            catch (Exception)
            {
                ErrorMessage = "We could not save your username because the account service is unavailable.";
            }
        }
    }
}
