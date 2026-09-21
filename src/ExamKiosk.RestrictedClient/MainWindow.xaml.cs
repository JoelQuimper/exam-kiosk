using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using ExamKiosk.Contracts;
using AppResources = ExamKiosk.Contracts.Resources;

namespace ExamKiosk.RestrictedClient;

public partial class MainWindow : Window
{
    private readonly AppBarDock appBarDock = new();
    private ActiveExamReference? activeExam;
    private bool actionInProgress;
    private bool allowClose;

    public MainWindow()
    {
        InitializeComponent();
        Title = AppResources.ExamSessionTitle;
        StatusHeading.Text = AppResources.ExamInProgress;
        StatusMessage.Text = AppResources.LoadingWebContent;
        OpenExamButton.Content = AppResources.OpenExam;
        RetryButton.Content = AppResources.Retry;
        FinishButton.Content = AppResources.ExamDone;
        Application.Current.SessionEnding += (_, _) => allowClose = true;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        appBarDock.Register(this, 320);
        await RefreshActiveExamAsync();
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshActiveExamAsync();
    }

    private async void OpenExamButton_Click(object sender, RoutedEventArgs e)
    {
        if (actionInProgress || activeExam is null)
        {
            return;
        }

        actionInProgress = true;
        SetButtonsEnabled(false);
        try
        {
            var response = await AgentClient.SendAsync(
                AgentCommand.GetActiveExam,
                TimeSpan.FromSeconds(15));
            if (!response.Success
                || response.State != AgentState.InExam
                || response.ActiveExam is not { } confirmed
                || confirmed.SessionId != activeExam.SessionId)
            {
                ShowUnavailable(AppResources.NoActiveExamSession);
                return;
            }

            activeExam = confirmed;
            Process.Start(CreateEdgeStartInfo(confirmed.EntryUrl));
            StatusMessage.Text = AppResources.ExamOpened;
        }
        catch
        {
            StatusMessage.Text = AppResources.ExamOpenFailed;
        }
        finally
        {
            actionInProgress = false;
            if (activeExam is not null)
            {
                SetButtonsEnabled(true);
            }
        }
    }

    private async void FinishButton_Click(object sender, RoutedEventArgs e)
    {
        if (actionInProgress)
        {
            return;
        }

        actionInProgress = true;
        SetButtonsEnabled(false);
        try
        {
            var dialog = new TransitionDialog(
                this,
                AppResources.FinishWarningTitle,
                AppResources.FinishWarning,
                AppResources.ExamDone,
                AppResources.LeavingExam,
                AppResources.ExamFinishFailed,
                () => AgentClient.SendAsync(
                    AgentCommand.FinishExam,
                    TimeSpan.FromSeconds(30)));
            dialog.ShowDialog();
            if (dialog.WasCancelled)
            {
                SetButtonsEnabled(true);
                return;
            }

            if (dialog.Response?.Success == true)
            {
                activeExam = null;
                ExamTitle.Visibility = Visibility.Collapsed;
                OpenExamButton.Visibility = Visibility.Collapsed;
                FinishButton.Visibility = Visibility.Collapsed;
                StatusMessage.Text = AppResources.LeavingExam;
                return;
            }

            StatusMessage.Text = AppResources.ExamFinishFailed;
            SetButtonsEnabled(true);
        }
        catch
        {
            StatusMessage.Text = AppResources.ExamFinishFailed;
            SetButtonsEnabled(true);
        }
        finally
        {
            actionInProgress = false;
        }
    }

    private async Task RefreshActiveExamAsync()
    {
        if (actionInProgress)
        {
            return;
        }

        actionInProgress = true;
        RetryButton.Visibility = Visibility.Collapsed;
        SetButtonsEnabled(false);
        StatusMessage.Text = AppResources.LoadingWebContent;
        try
        {
            var response = await AgentClient.SendAsync(
                AgentCommand.GetActiveExam,
                TimeSpan.FromSeconds(15));
            if (!response.Success
                || response.State != AgentState.InExam
                || response.ActiveExam is not { } exam)
            {
                ShowUnavailable(
                    response.Success
                        ? AppResources.NoActiveExamSession
                        : response.Message);
                return;
            }

            activeExam = exam;
            ExamTitle.Text = exam.Title;
            ExamTitle.Visibility = Visibility.Visible;
            StatusMessage.Text = AppResources.ExamSessionReady;
            OpenExamButton.Visibility = Visibility.Visible;
            FinishButton.Visibility = Visibility.Visible;
            SetButtonsEnabled(true);
        }
        catch
        {
            ShowUnavailable(AppResources.AgentStatusUnavailable);
        }
        finally
        {
            actionInProgress = false;
        }
    }

    private void ShowUnavailable(string message)
    {
        activeExam = null;
        ExamTitle.Visibility = Visibility.Collapsed;
        OpenExamButton.Visibility = Visibility.Collapsed;
        FinishButton.Visibility = Visibility.Collapsed;
        RetryButton.Visibility = Visibility.Visible;
        RetryButton.IsEnabled = true;
        StatusMessage.Text = message;
    }

    private void SetButtonsEnabled(bool enabled)
    {
        OpenExamButton.IsEnabled = enabled;
        FinishButton.IsEnabled = enabled;
    }

    internal static ProcessStartInfo CreateEdgeStartInfo(Uri examUri)
    {
        ArgumentNullException.ThrowIfNull(examUri);
        if (!examUri.IsAbsoluteUri
            || examUri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(examUri.Host)
            || !string.IsNullOrEmpty(examUri.UserInfo))
        {
            throw new ArgumentException(
                "The active exam destination must be an absolute HTTPS URL.",
                nameof(examUri));
        }

        var edgePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft",
            "Edge",
            "Application",
            "msedge.exe");
        var startInfo = new ProcessStartInfo
        {
            FileName = edgePath,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--new-window");
        startInfo.ArgumentList.Add("--start-maximized");
        startInfo.ArgumentList.Add("--no-first-run");
        startInfo.ArgumentList.Add("--inprivate");
        startInfo.ArgumentList.Add(examUri.AbsoluteUri);
        return startInfo;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!allowClose)
        {
            e.Cancel = true;
            return;
        }

        appBarDock.Dispose();
    }
}
