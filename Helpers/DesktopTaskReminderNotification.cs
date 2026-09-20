using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace DMS.Helpers
{
    public static class DesktopTaskReminderNotification
    {
        private static readonly object SyncRoot = new();
        private static Window? _notificationWindow;
        private static DispatcherTimer? _closeTimer;

        public static void Show(string title, string message)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
                return;

            dispatcher.BeginInvoke(new Action(() =>
            {
                lock (SyncRoot)
                {
                    if (_notificationWindow != null)
                    {
                        _notificationWindow.Activate();
                        return;
                    }

                    var workArea = SystemParameters.WorkArea;
                    var width = 420d;
                    var height = 128d;
                    var left = workArea.Right - width - 24;
                    var top = workArea.Bottom - height - 24;

                    _notificationWindow = new Window
                    {
                        Width = width,
                        Height = height,
                        Left = left,
                        Top = top,
                        Topmost = true,
                        ShowInTaskbar = false,
                        WindowStyle = WindowStyle.None,
                        AllowsTransparency = true,
                        Background = Brushes.Transparent,
                        ResizeMode = ResizeMode.NoResize,
                        WindowStartupLocation = WindowStartupLocation.Manual,
                        ShowActivated = false,
                        Focusable = false,
                        Title = title
                    };

                    _notificationWindow.Content = CreateContent(title, message);
                    _notificationWindow.MouseLeftButtonDown += (_, _) => Close();
                    _notificationWindow.Closed += (_, _) =>
                    {
                        lock (SyncRoot)
                        {
                            _notificationWindow = null;
                            _closeTimer?.Stop();
                            _closeTimer = null;
                        }
                    };

                    _notificationWindow.Show();

                    _closeTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(15)
                    };
                    _closeTimer.Tick += (_, _) => Close();
                    _closeTimer.Start();
                }
            }));
        }

        public static void Close()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
                return;

            dispatcher.BeginInvoke(new Action(() =>
            {
                lock (SyncRoot)
                {
                    _closeTimer?.Stop();
                    _notificationWindow?.Close();
                }
            }));
        }

        private static UIElement CreateContent(string title, string message)
        {
            var outer = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(255, 247, 220)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(214, 69, 69)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(0),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    ShadowDepth = 8,
                    Opacity = 0.28,
                    BlurRadius = 16
                }
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var accent = new Border
            {
                Width = 68,
                Background = new SolidColorBrush(Color.FromRgb(214, 69, 69)),
                CornerRadius = new CornerRadius(14, 0, 0, 14)
            };
            var exclamation = new TextBlock
            {
                Text = "!",
                Foreground = Brushes.White,
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            accent.Child = exclamation;
            Grid.SetColumn(accent, 0);

            var contentStack = new StackPanel
            {
                Margin = new Thickness(16, 12, 16, 12),
                VerticalAlignment = VerticalAlignment.Center
            };

            var header = new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush(Color.FromRgb(109, 43, 27)),
                FontWeight = FontWeights.Bold,
                FontSize = 18
            };

            var body = new TextBlock
            {
                Text = message,
                Foreground = new SolidColorBrush(Color.FromRgb(109, 43, 27)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };

            contentStack.Children.Add(header);
            contentStack.Children.Add(body);
            Grid.SetColumn(contentStack, 1);

            grid.Children.Add(accent);
            grid.Children.Add(contentStack);
            outer.Child = grid;
            return outer;
        }
    }
}
