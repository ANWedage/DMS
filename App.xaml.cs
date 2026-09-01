using System.Windows;
using DMS.Data;
using DMS.Services;
using DMS.Views;

namespace DMS
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            MongoDbContext? context = null;
            try
            {
                context = new MongoDbContext();
                context.EnsureIndexes();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"DMS could not reach its database. The app will continue in offline mode, but account actions will be unavailable until the MongoDB connection is restored.\n\nDetails: {ex.Message}",
                    "Database connection unavailable",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            context ??= new MongoDbContext();
            IUserService userService = new UserService(context);

            var loginWindow = new LoginWindow(userService);
            loginWindow.Show();
        }
    }
}
