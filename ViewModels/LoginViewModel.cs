using System;
using DMS.Data;
using DMS.Helpers;
using DMS.Models;
using DMS.Services;
using System.Threading.Tasks;

namespace DMS.ViewModels
{
    public class LoginViewModel : ViewModelBase
    {
        private readonly IUserService _userService;

        public RegisterViewModel Register { get; }

        private bool _isRegistering;
        public bool IsRegistering
        {
            get => _isRegistering;
            set => SetProperty(ref _isRegistering, value);
        }

        private bool _isAdminLogin;
        public bool IsAdminLogin
        {
            get => _isAdminLogin;
            set
            {
                if (SetProperty(ref _isAdminLogin, value))
                {
                    OnPropertyChanged(nameof(LoginRoleLabel));
                    OnPropertyChanged(nameof(LoginButtonText));
                }
            }
        }

        public string LoginRoleLabel => IsAdminLogin ? "Admin login" : "User login";
        public string LoginButtonText => IsAdminLogin ? "Sign in as admin" : "Sign in";

        private bool _isConnecting;
        public bool IsConnecting
        {
            get => _isConnecting;
            private set
            {
                if (SetProperty(ref _isConnecting, value))
                {
                    OnPropertyChanged(nameof(CanLogin));
                    LoginCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool CanLogin => !IsConnecting;

        private string _connectionStatus = "Ready to connect";
        public string ConnectionStatus
        {
            get => _connectionStatus;
            private set => SetProperty(ref _connectionStatus, value);
        }

        private bool _hasConnectionError;
        public bool HasConnectionError
        {
            get => _hasConnectionError;
            private set => SetProperty(ref _hasConnectionError, value);
        }

        private string _username = string.Empty;
        public string Username
        {
            get => _username;
            set => SetProperty(ref _username, value);
        }

        // Set from code-behind (PasswordBox can't bind directly)
        public string Password { get; set; } = string.Empty;

        private string _errorMessage = string.Empty;
        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public RelayCommand LoginCommand { get; }
        public RelayCommand CreateAccountCommand { get; }
        public RelayCommand BackToLoginCommand { get; }
        public RelayCommand ToggleRoleCommand { get; }

        /// <summary>Raised with the logged-in user when login succeeds.</summary>
        public Action<User>? LoginSucceeded { get; set; }

        /// <summary>Raised when the user wants to go to the create-account screen.</summary>
        public Action? NavigateToRegister { get; set; }

        /// <summary>Raised when the user returns to the sign-in screen.</summary>
        public Action? NavigateToLogin { get; set; }

        public LoginViewModel(IUserService userService)
        {
            _userService = userService;
            Register = new RegisterViewModel(userService);
            LoginCommand = new RelayCommand(_ => LoginAsync(), _ => CanLogin);
            ToggleRoleCommand = new RelayCommand(_ => ToggleRole());
            CreateAccountCommand = new RelayCommand(_ =>
            {
                IsRegistering = true;
                NavigateToRegister?.Invoke();
            });
            BackToLoginCommand = new RelayCommand(_ =>
            {
                Register.Email = string.Empty;
                Register.ContactNumber = string.Empty;
                Register.Password = string.Empty;
                Register.ConfirmPassword = string.Empty;
                Register.ErrorMessage = string.Empty;
                IsRegistering = false;
                NavigateToLogin?.Invoke();
            });
        }

        public void ToggleRole()
        {
            IsAdminLogin = !IsAdminLogin;
            ErrorMessage = string.Empty;
            ConnectionStatus = "Ready to connect";
            HasConnectionError = false;
            Username = string.Empty;
            Password = string.Empty;
            OnPropertyChanged(nameof(LoginRoleLabel));
            OnPropertyChanged(nameof(LoginButtonText));
        }

        private async void LoginAsync()
        {
            ErrorMessage = string.Empty;

            var normalizedUsername = Username.Trim();
            if (string.IsNullOrWhiteSpace(normalizedUsername) || string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "Enter your username and password.";
                return;
            }

            IsConnecting = true;
            ConnectionStatus = "Connecting to server...";
            HasConnectionError = false;

            try
            {
                if (IsAdminLogin)
                {
                    var admin = await Task.Run(() => _userService.LoginAdmin(normalizedUsername, Password));
                    ConnectionStatus = "Server connected";

                    if (admin is null)
                    {
                        ErrorMessage = "Invalid admin credentials.";
                        return;
                    }

                    AppSession.SetAdmin(admin.Name, admin.Username, admin.Id);
                    LoginSucceeded?.Invoke(new User
                    {
                        Id = admin.Id,
                        Email = $"{admin.Username}@dms.local",
                        ContactNumber = "0000000000",
                        Username = admin.Username
                    });
                    return;
                }

                if (!SecurityValidator.IsValidUsername(normalizedUsername))
                {
                    ErrorMessage = "Username is invalid.";
                    return;
                }

                var user = await Task.Run(() => _userService.Login(normalizedUsername, Password));
                ConnectionStatus = "Server connected";

                if (user is null)
                {
                    ErrorMessage = "Invalid username or password.";
                    return;
                }

                AppSession.SetCurrentUser(user);
                LoginSucceeded?.Invoke(user);
            }
            catch (AccountDisabledException ex)
            {
                ConnectionStatus = "Server connected";
                ErrorMessage = ex.Message;
            }
            catch (Exception)
            {
                ConnectionStatus = "Unable to reach server";
                HasConnectionError = true;
                ErrorMessage = IsAdminLogin
                    ? "We could not connect to the admin service. Check the database connection and try again."
                    : "We could not connect to the account service. Check the database connection and try again.";
            }
            finally
            {
                IsConnecting = false;
            }
        }
    }
}
