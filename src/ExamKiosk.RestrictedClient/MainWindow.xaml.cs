using System.Diagnostics;
using System.IO;
using System.Windows;
using ExamKiosk.Contracts;

namespace ExamKiosk.RestrictedClient;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OpenExamButton_Click(object sender, RoutedEventArgs e)
    {
        var edgePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft",
            "Edge",
            "Application",
            "msedge.exe");

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = edgePath,
                Arguments = "https://www.example.com",
                UseShellExecute = true
            });
            StatusText.Text = "The placeholder exam was opened in Edge.";
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Microsoft Edge could not be opened.\n\n{exception.Message}",
                "Bogus exam",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void FinishExamButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                this,
                "Finishing the exam will remove the restricted session and restart Windows. " +
                "Make sure your exam work has been saved.\n\nDo you want to finish?",
                "Finish bogus exam",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        FinishExamButton.IsEnabled = false;
        StatusText.Text = "Asking the Exam Device Agent to leave exam mode...";

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
                    "Bogus exam",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                FinishExamButton.IsEnabled = true;
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                "The Exam Device Agent could not be reached. Contact the test administrator.\n\n" +
                exception.Message,
                "Bogus exam",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "The exam could not be finished.";
            FinishExamButton.IsEnabled = true;
        }
    }
}