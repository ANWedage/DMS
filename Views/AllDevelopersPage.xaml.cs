using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DMS.Models;
using DMS.Services;

namespace DMS.Views
{
    public partial class AllDevelopersPage : Page
    {
        private readonly IUserService _userService;
        private List<User> _allUsers = new();

        public AllDevelopersPage(IUserService userService)
        {
            InitializeComponent();
            _userService = userService;
            Loaded += AllDevelopersPage_Loaded;
        }

        private async void AllDevelopersPage_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var users = await Task.Run(() => _userService.GetAllUsers());
                _allUsers = users
                    .OrderBy(u => string.IsNullOrWhiteSpace(u.Username) ? u.Email : u.Username)
                    .ToList();

                ApplyFilter();
            }
            catch (Exception ex)
            {
                DeveloperListView.ItemsSource = new List<User>();
                MessageBox.Show($"Unable to load developers: {ex.Message}", "Developer list", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Clear();
            SearchTextBox.Focus();
        }

        private void ApplyFilter()
        {
            var searchText = SearchTextBox?.Text ?? string.Empty;
            var filteredUsers = string.IsNullOrWhiteSpace(searchText)
                ? _allUsers
                : _allUsers.Where(u =>
                    (u.Username ?? string.Empty).Contains(searchText, StringComparison.OrdinalIgnoreCase)
                    || (u.Email ?? string.Empty).Contains(searchText, StringComparison.OrdinalIgnoreCase)
                    || (u.ContactNumber ?? string.Empty).Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .ToList();

            DeveloperListView.ItemsSource = filteredUsers;
        }
    }
}
