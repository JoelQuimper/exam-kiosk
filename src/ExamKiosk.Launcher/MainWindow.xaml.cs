using System.Windows;
using ExamKiosk.Contracts;

namespace ExamKiosk.Launcher;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void StartExamButton_Click(object sender, RoutedEventArgs e)
    {
        const string warning =
            "The Exam Device Agent will apply an Assigned Access profile and restart this " +
            "computer into a restricted local exam account.\n\n" +
            "Save your work before continuing. Do you want to start the bogus exam?";

        if (MessageBox.Show(
                this,
                warning,
                "Start bogus exam",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        StartExamButton.IsEnabled = false;
        StatusText.Text = "Asking the Exam Device Agent to prepare the device...";

        try
        {
            var response = await AgentClient.SendAsync(
                AgentCommand.StartExam,
                TimeSpan.FromSeconds(30));

            StatusText.Text = response.Message;
            if (!response.Success)
            {
                MessageBox.Show(
                    this,
                    response.Message,
                    "Exam Kiosk",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                StartExamButton.IsEnabled = true;
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                "The Exam Device Agent could not be reached. Verify that the service is " +
                $"installed and running.\n\n{exception.Message}",
                "Exam Kiosk",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = "Kiosk preparation failed to start.";
            StartExamButton.IsEnabled = true;
        }
    }
}