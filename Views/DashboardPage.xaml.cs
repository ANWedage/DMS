using System.Threading.Tasks;
using System.Windows.Controls;
using DMS.Services;

namespace DMS.Views
{
    public partial class DashboardPage : Page
    {
        private readonly IUserService _userService;

        public DashboardPage(IUserService userService)
        {
            InitializeComponent();
            _userService = userService;
            Loaded += DashboardPage_Loaded;
        }

        private async void DashboardPage_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                var activeUserCount = await Task.Run(() => _userService.GetActiveUserCount());
                ActiveUsersCountText.Text = activeUserCount.ToString();
            }
            catch
            {
                ActiveUsersCountText.Text = "N/A";
            }
        }
    }
}
