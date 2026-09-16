using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ExamKiosk.Contracts;
using AppResources = ExamKiosk.Contracts.Resources;

namespace ExamKiosk.RestrictedClient;

internal sealed class RestartCountdownWindow : Window
{
    private readonly DateTimeOffset restartAtUtc;
    private readonly TextBlock countdown;
    private readonly DispatcherTimer timer;

    internal RestartCountdownWindow(Window owner, DateTimeOffset restartAtUtc)
    {
        this.restartAtUtc = restartAtUtc;
        Owner = owner;
        Title = AppResources.RestartingWindows;
        Width = 380;
        Height = 220;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        Topmost = true;
        Background = new SolidColorBrush(Color.FromRgb(245, 247, 251));

        countdown = new TextBlock
        {
            Margin = new Thickness(0, 18, 0, 0),
            FontSize = 54,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(36, 87, 230)),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        Content = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = AppResources.RestartingWindows,
                    FontSize = 24,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(20, 33, 61)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
                countdown,
            },
        };

        PreviewKeyDown += (_, args) =>
        {
            if (args.SystemKey == Key.F4 && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                args.Handled = true;
            }
        };
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) => UpdateCountdown();
        Loaded += (_, _) =>
        {
            UpdateCountdown();
            timer.Start();
        };
    }

    private void UpdateCountdown()
    {
        var remainingSeconds = RestartSchedule.GetRemainingSeconds(
            restartAtUtc,
            DateTimeOffset.UtcNow);
        countdown.Text = TimeSpan
            .FromSeconds(remainingSeconds)
            .ToString(@"mm\:ss", CultureInfo.InvariantCulture);
        if (remainingSeconds > 0)
        {
            return;
        }

        timer.Stop();
        Close();
    }
}
