using System.Diagnostics;
using System.IO;
using System.Windows;
using ExamKiosk.Contracts;
using AppResources = ExamKiosk.Contracts.Resources;

namespace ExamKiosk.RestrictedClient;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        Title = AppResources.ExamSessionTitle;
        ExamProgressLabelText.Text = AppResources.ExamInProgress;
        ExamTitleText.Text = AppResources.ExamTitle;
        RestrictedSessionText.Text = AppResources.RestrictedSession;
        ExamInstructionsText.Text = AppResources.Instructions;
        OpenExamButton.Content = AppResources.OpenExam;
        FinishExamButton.Content = AppResources.ExamDone;
        StatusText.Text = AppResources.Connected;
    }

    private void OpenExamButton_Click(object sender, RoutedEventArgs e)
    {
        var edgePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft",
            "Edge",
            "Application",
            "msedge.exe");
        const string examUrl = "https://www.example.com";
        var edgeArguments = $"--new-window --no-first-run --inprivate {examUrl}";

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = edgePath,
                Arguments = edgeArguments,
                UseShellExecute = true
            });
            StatusText.Text = AppResources.ExamOpened;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                AppResources.EdgeOpenFailed + exception.Message,
                AppResources.ExamTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void FinishExamButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                this,
                AppResources.FinishWarning,
                AppResources.FinishWarningTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        FinishExamButton.IsEnabled = false;
        StatusText.Text = AppResources.LeavingExam;

        try
        {
            var response = await AgentClient.SendAsync(
                AgentCommand.FinishExam,
                TimeSpan.FromSeconds(30));
            StatusText.Text = response.Message;

            if (!response.Success)
            {
                MessageBox.Show(
                    this,
                    response.Message,
                    AppResources.ExamTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                FinishExamButton.IsEnabled = true;
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                AppResources.AgentUnreachableFinish + exception.Message,
                AppResources.ExamTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = AppResources.ExamFinishFailed;
            FinishExamButton.IsEnabled = true;
        }
    }
}