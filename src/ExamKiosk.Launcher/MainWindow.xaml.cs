using System.Windows;
using ExamKiosk.Contracts;
using AppResources = ExamKiosk.Contracts.Resources;

namespace ExamKiosk.Launcher;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        Title = AppResources.KioskTitle;
        KioskLabelText.Text = AppResources.KioskLabel;
        AvailableExamsText.Text = AppResources.AvailableExams;
        ReadyText.Text = AppResources.Ready;
        ExamTitleText.Text = AppResources.ExamTitle;
        ExamDescriptionText.Text = AppResources.ExamDescription;
        ExamDetailsText.Text = AppResources.ExamDetails;
        StartExamButton.Content = AppResources.StartExam;
        StatusText.Text = AppResources.NoExamRunning;
    }

    private async void StartExamButton_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(
                this,
                AppResources.StartWarning,
                AppResources.StartWarningTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        StartExamButton.IsEnabled = false;
        StatusText.Text = AppResources.PreparingDevice;

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
                    AppResources.KioskTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                StartExamButton.IsEnabled = true;
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                AppResources.AgentUnreachableStart + exception.Message,
                AppResources.KioskTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            StatusText.Text = AppResources.PreparationFailed;
            StartExamButton.IsEnabled = true;
        }
    }
}