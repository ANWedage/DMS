using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace DMS.Helpers
{
    public sealed class SessionInactivityMonitor : IDisposable
    {
        private static readonly TimeSpan InactivityTimeout = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan WarningStartTime = TimeSpan.FromSeconds(170);
        private static readonly TimeSpan WarningDuration = TimeSpan.FromSeconds(10);

        public event EventHandler<TimeSpan>? RemainingTimeChanged;

        private readonly Window _window;
        private readonly Action _onTimeout;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly object _lock = new();
        private DateTimeOffset _lastActivityUtc = DateTimeOffset.UtcNow;
        private Window? _warningWindow;
        private bool _disposed;

        public SessionInactivityMonitor(Window window, Action onTimeout)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _onTimeout = onTimeout ?? throw new ArgumentNullException(nameof(onTimeout));

            _timer.Tick += OnTimerTick;

            _window.PreviewMouseMove += (_, _) => ResetActivity();
            _window.PreviewMouseDown += (_, _) => ResetActivity();
            _window.PreviewKeyDown += (_, _) => ResetActivity();
            _window.Activated += (_, _) => ResetActivity();
            _window.Closed += (_, _) => Dispose();
        }

        public void Start()
        {
            if (_disposed)
                return;

            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
            CloseWarning();
        }

        public TimeSpan RemainingTime =>
            InactivityTimeout - (DateTimeOffset.UtcNow - _lastActivityUtc);

        public void ResetActivity()
        {
            lock (_lock)
            {
                _lastActivityUtc = DateTimeOffset.UtcNow;
                CloseWarning();
            }

            NotifyRemainingTimeChanged();
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            if (_disposed)
                return;

            var inactivity = DateTimeOffset.UtcNow - _lastActivityUtc;
            NotifyRemainingTimeChanged();

            if (inactivity >= InactivityTimeout)
            {
                Stop();
                _onTimeout();
                return;
            }

            if (inactivity >= WarningStartTime)
            {
                ShowWarning();
            }
        }

        private void NotifyRemainingTimeChanged()
        {
            var remaining = RemainingTime;
            if (remaining < TimeSpan.Zero)
                remaining = TimeSpan.Zero;

            RemainingTimeChanged?.Invoke(this, remaining);
        }

        private void ShowWarning()
        {
            if (_disposed || _window.Dispatcher.HasShutdownStarted)
                return;

            lock (_lock)
            {
                if (_warningWindow != null || !(_window.IsVisible && _window.IsLoaded))
                    return;

                _warningWindow = new Window
                {
                    Owner = _window,
                    WindowStyle = WindowStyle.ToolWindow,
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false,
                    Topmost = true,
                    Width = 360,
                    Height = 160,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Title = "Session timeout warning",
                    Background = new SolidColorBrush(Color.FromRgb(255, 248, 214)),
                    Content = CreateWarningContent()
                };

                _warningWindow.Closed += (_, _) =>
                {
                    lock (_lock)
                    {
                        _warningWindow = null;
                    }
                };

                _warningWindow.Show();
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                var remainingTime = WarningDuration;
                var content = (StackPanel)_warningWindow.Content;
                var countdown = (TextBlock)content.Children[1];

                timer.Tick += (_, _) =>
                {
                    remainingTime = remainingTime.Subtract(TimeSpan.FromSeconds(1));
                    countdown.Text = $"This session will sign out in {Math.Max(remainingTime.Seconds, 0)} second(s).";
                    if (remainingTime <= TimeSpan.Zero)
                    {
                        timer.Stop();
                        _warningWindow?.Close();
                        if (DateTimeOffset.UtcNow - _lastActivityUtc >= InactivityTimeout)
                        {
                            _onTimeout();
                        }
                    }
                };

                timer.Start();
            }
        }

        private static UIElement CreateWarningContent()
        {
            var stack = new StackPanel
            {
                Margin = new Thickness(18),
                VerticalAlignment = VerticalAlignment.Center
            };

            var title = new TextBlock
            {
                Text = "Inactive session",
                FontWeight = FontWeights.Bold,
                FontSize = 20,
                Foreground = new SolidColorBrush(Color.FromRgb(70, 61, 32))
            };

            var message = new TextBlock
            {
                Text = "This session will sign out in 10 second(s).",
                Margin = new Thickness(0, 10, 0, 0),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(60, 59, 34))
            };

            stack.Children.Add(title);
            stack.Children.Add(message);
            return stack;
        }

        private void CloseWarning()
        {
            if (_warningWindow == null)
                return;

            _warningWindow.Dispatcher.BeginInvoke(new Action(() => _warningWindow.Close()));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Stop();
            _timer.Tick -= OnTimerTick;
        }
    }
}
