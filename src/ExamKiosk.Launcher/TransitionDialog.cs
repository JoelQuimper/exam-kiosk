using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ExamKiosk.Contracts;
using AppResources = ExamKiosk.Contracts.Resources;

namespace ExamKiosk.Launcher;

internal sealed class TransitionDialog : Window
{
    private readonly Func<Task<AgentResponse>> executeTransition;
    private readonly string workingMessage;
    private readonly string failureMessage;
    private readonly TextBlock heading;
    private readonly TextBlock message;
    private readonly TextBlock countdown;
    private readonly StackPanel actions;
    private readonly Button confirmButton;
    private readonly Button cancelButton;
    private readonly DispatcherTimer timer;
    private DateTimeOffset restartAtUtc;
    private bool transitionStarted;

    internal TransitionDialog(
        Window owner,
        string title,
        string warning,
        string confirmText,
        string workingMessage,
        string failureMessage,
        Func<Task<AgentResponse>> executeTransition)
    {
        this.workingMessage = workingMessage;
        this.failureMessage = failureMessage;
        this.executeTransition = executeTransition;
        Owner = owner;
        Title = title;
        Width = 520;
        Height = 360;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;

        heading = new TextBlock
        {
            Text = title,
            FontSize = 26,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(20, 33, 61)),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        message = new TextBlock
        {
            Text = warning,
            Margin = new Thickness(0, 18, 0, 0),
            FontSize = 15,
            Foreground = new SolidColorBrush(Color.FromRgb(98, 112, 138)),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        countdown = new TextBlock
        {
            Margin = new Thickness(0, 18, 0, 0),
            FontSize = 54,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(36, 87, 230)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        cancelButton = CreateButton(AppResources.Cancel, Color.FromRgb(255, 255, 255), Color.FromRgb(20, 33, 61));
        cancelButton.BorderBrush = new SolidColorBrush(Color.FromRgb(211, 218, 232));
        cancelButton.BorderThickness = new Thickness(1);
        cancelButton.Click += (_, _) => Close();
        confirmButton = CreateButton(confirmText, Color.FromRgb(36, 87, 230), Colors.White);
        confirmButton.Click += ConfirmButton_Click;
        actions = new StackPanel
        {
            Margin = new Thickness(0, 28, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            Orientation = Orientation.Horizontal,
            Children = { cancelButton, confirmButton },
        };

        var content = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                heading,
                message,
                countdown,
                actions,
            },
        };
        Content = new Border
        {
            Padding = new Thickness(42),
            Background = new SolidColorBrush(Color.FromRgb(245, 247, 251)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(211, 218, 232)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Child = content,
        };

        PreviewKeyDown += (_, args) =>
        {
            if (transitionStarted
                && args.SystemKey == Key.F4
                && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                args.Handled = true;
            }
        };
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (_, _) => UpdateCountdown();
    }

    internal AgentResponse? Response { get; private set; }

    internal bool WasCancelled => !transitionStarted;

    private async void ConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        transitionStarted = true;
        actions.Visibility = Visibility.Collapsed;
        message.Text = workingMessage;

        try
        {
            Response = await executeTransition();
            if (!Response.Success || Response.RestartAtUtc is not { } scheduledRestart)
            {
                ShowFailure();
                return;
            }

            restartAtUtc = scheduledRestart;
            heading.Text = AppResources.RestartingWindows;
            message.Visibility = Visibility.Collapsed;
            countdown.Visibility = Visibility.Visible;
            UpdateCountdown();
            timer.Start();
        }
        catch (Exception)
        {
            ShowFailure();
        }
    }

    private void ShowFailure()
    {
        heading.Text = failureMessage;
        message.Visibility = Visibility.Collapsed;
        countdown.Visibility = Visibility.Collapsed;
        cancelButton.Visibility = Visibility.Collapsed;
        confirmButton.Content = AppResources.Close;
        confirmButton.Click -= ConfirmButton_Click;
        confirmButton.Click += (_, _) => Close();
        actions.Visibility = Visibility.Visible;
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

    private static Button CreateButton(string text, Color background, Color foreground)
    {
        return new Button
        {
            Content = text,
            MinWidth = 130,
            Margin = new Thickness(6, 0, 6, 0),
            Padding = new Thickness(18, 11, 18, 11),
            Background = new SolidColorBrush(background),
            Foreground = new SolidColorBrush(foreground),
            BorderThickness = new Thickness(0),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
        };
    }
}
