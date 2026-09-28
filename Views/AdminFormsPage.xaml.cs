using System.Windows;
using System.Windows.Controls;
using DMS.Helpers;
using DMS.Services;

namespace DMS.Views;

public partial class AdminFormsPage : Page
{
    private readonly IUserService _userService;

    public AdminFormsPage(IUserService userService)
    {
        InitializeComponent();
        _userService = userService;
        Loaded += async (_, _) => await LoadFormLinksAsync();
    }

    private async Task LoadFormLinksAsync()
    {
        try
        {
            var settings = await Task.Run(_userService.GetMeetingSettings);
            LeaveFormLinkBox.Text = settings.LeaveFormLink;
        }
        catch (Exception ex)
        {
            FormsMessageText.Text = $"Unable to load form links: {ex.Message}";
        }
    }

    private async void SaveFormLinksButton_Click(object sender, RoutedEventArgs e)
    {
        FormsMessageText.Text = string.Empty;
        if (MessageBox.Show("Save the leave form link?", "Confirm form link", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            var settings = await Task.Run(_userService.GetMeetingSettings);
            settings.LeaveFormLink = LeaveFormLinkBox.Text.Trim();
            await Task.Run(() => _userService.SaveMeetingSettings(settings,
                AppSession.CurrentUserId ?? string.Empty,
                AppSession.CurrentDisplayName ?? AppSession.CurrentUsername ?? "Administrator"));
            FormsMessageText.Foreground = System.Windows.Media.Brushes.DarkGreen;
            FormsMessageText.Text = "Form links saved.";
        }
        catch (Exception ex)
        {
            FormsMessageText.Foreground = System.Windows.Media.Brushes.Firebrick;
            FormsMessageText.Text = ex.Message;
        }
    }
}
