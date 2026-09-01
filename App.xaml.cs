using System.Windows;
using DMS.Data;
using DMS.Helpers;
using DMS.Models;
using DMS.Services;
using DMS.Views;
using MongoDB.Driver;

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
                SeedAdminUsers(context);
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

        private static void SeedAdminUsers(MongoDbContext context)
        {
            var configuredAdmins = AdminConfig.GetConfiguredAdmins();

            foreach (var admin in configuredAdmins)
            {
                var existing = context.Admins.Find(a => a.Username == admin.Username).FirstOrDefault();
                if (existing != null)
                    continue;

                var (hash, salt) = PasswordHasher.HashPassword(admin.Password);
                context.Admins.InsertOne(new AdminUser
                {
                    Name = admin.Name,
                    Username = admin.Username,
                    PasswordHash = hash,
                    PasswordSalt = salt
                });
            }
        }
    }
}
