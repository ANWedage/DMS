using System.Diagnostics;
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

    private void LeaveFormLinkBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        OpenLeaveFormButton.IsEnabled = TryGetLeaveFormUri(out _);
    }

    private void OpenLeaveFormButton_Click(object sender, RoutedEventArgs e)
    {
        FormsMessageText.Text = string.Empty;
        if (!TryGetLeaveFormUri(out var uri))
        {
            FormsMessageText.Text = "Enter a valid http or https leave form URL.";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri!.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            FormsMessageText.Text = $"Unable to open the leave form link: {ex.Message}";
        }
    }

    private bool TryGetLeaveFormUri(out Uri? uri)
    {
        var link = LeaveFormLinkBox.Text.Trim();
        if (Uri.TryCreate(link, UriKind.Absolute, out var parsedUri)
            && (parsedUri.Scheme == Uri.UriSchemeHttp || parsedUri.Scheme == Uri.UriSchemeHttps))
        {
            uri = parsedUri;
            return true;
        }

        uri = null;
        return false;
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
            FormsMessageText.Text = string.Empty;
            MessageBox.Show("Meeting form links saved successfully.",
                "Meeting details saved", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            FormsMessageText.Foreground = System.Windows.Media.Brushes.Firebrick;
            FormsMessageText.Text = ex.Message;
        }
    }
}
