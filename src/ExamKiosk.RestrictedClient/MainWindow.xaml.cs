using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using ExamKiosk.Contracts;
using Microsoft.Web.WebView2.Core;
using AppResources = ExamKiosk.Contracts.Resources;

namespace ExamKiosk.RestrictedClient;

public partial class MainWindow : Window
{
    private static readonly Uri FixedExamUri = new("https://www.example.com/");
    private static readonly JsonSerializerOptions BridgeSerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly string profilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ExamKiosk",
        "RestrictedClientWebView2");
    private readonly WebViewDiagnosticLog diagnosticLog = new("restricted-client");
    private WebViewHostConfiguration? configuration;
    private WebViewNavigationPolicy? navigationPolicy;
    private bool initializationInProgress;
    private bool actionInProgress;
    private bool cleanupInProgress;
    private bool closeAfterCleanup;
    private bool closeCheckInProgress;
    private bool navigationWasBlocked;

    public MainWindow()
    {
        InitializeComponent();
        Title = AppResources.ExamSessionTitle;
        ShowLoading();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await InitializeBrowserAsync();
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        await InitializeBrowserAsync();
    }

    private async void NativeFinishButton_Click(object sender, RoutedEventArgs e)
    {
        await FinishExamAsync(requestId: null);
    }

    private async Task InitializeBrowserAsync()
    {
        if (initializationInProgress)
        {
            return;
        }

        initializationInProgress = true;
        ShowLoading();
        diagnosticLog.Write("initialization-started");

        try
        {
            configuration ??= WebViewHostConfiguration.Load(
                "restricted-client.settings.json",
                "/exam-session");
            diagnosticLog.Write(
                "configuration-loaded",
                new { page = WebViewDiagnosticLog.DescribeUri(configuration.PageUri.AbsoluteUri) });
            navigationPolicy ??= new WebViewNavigationPolicy(
                configuration,
                [new Uri("https://login.microsoftonline.com")]);

            if (Browser.CoreWebView2 is null)
            {
                await DeleteProfileDirectoryWithRetryAsync();
                var environmentOptions = new CoreWebView2EnvironmentOptions
                {
                    AreBrowserExtensionsEnabled = false,
                };
                var environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: profilePath,
                    options: environmentOptions);
                await Browser.EnsureCoreWebView2Async(environment);
                if (cleanupInProgress || closeAfterCleanup)
                {
                    return;
                }

                ConfigureBrowser();
                diagnosticLog.Write("webview-configured");
            }

            var core = Browser.CoreWebView2
                ?? throw new InvalidOperationException("WebView2 initialization did not complete.");
            if (cleanupInProgress || closeAfterCleanup)
            {
                return;
            }

            core.Navigate(configuration.PageUri.AbsoluteUri);
        }
        catch (Exception exception)
        {
            diagnosticLog.Write(
                "initialization-failed",
                new { exceptionType = exception.GetType().FullName, exception.Message });
            var details = exception is InvalidDataException
                ? AppResources.RestrictedClientConfigurationInvalid
                : AppResources.WebNavigationFailed;
            ShowFailure(AppResources.WebContentUnavailable, details);
        }
        finally
        {
            initializationInProgress = false;
        }
    }

    private void ConfigureBrowser()
    {
        var core = Browser.CoreWebView2
            ?? throw new InvalidOperationException("WebView2 initialization did not complete.");
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;

        core.NavigationStarting += Browser_NavigationStarting;
        core.FrameNavigationStarting += Browser_FrameNavigationStarting;
        core.NavigationCompleted += Browser_NavigationCompleted;
        core.ProcessFailed += Browser_ProcessFailed;
        core.PermissionRequested += (_, args) =>
        {
            args.State = CoreWebView2PermissionState.Deny;
            args.Handled = true;
        };
        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.DownloadStarting += (_, args) =>
        {
            args.Cancel = true;
            args.Handled = true;
        };
        core.WebMessageReceived += Browser_WebMessageReceived;
    }

    private void Browser_NavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs e)
    {
        var allowed = navigationPolicy?.IsAllowed(e.Uri) == true;
        diagnosticLog.Write(
            "navigation-starting",
            new { target = WebViewDiagnosticLog.DescribeUri(e.Uri), allowed });
        if (allowed)
        {
            return;
        }

        e.Cancel = true;
        navigationWasBlocked = true;
        ShowFailure(AppResources.NavigationBlocked, AppResources.NavigationBlockedDetails);
    }

    private void Browser_FrameNavigationStarting(
        object? sender,
        CoreWebView2NavigationStartingEventArgs e)
    {
        if (navigationPolicy?.IsAllowed(e.Uri) != true)
        {
            diagnosticLog.Write(
                "frame-navigation-blocked",
                new { target = WebViewDiagnosticLog.DescribeUri(e.Uri) });
            e.Cancel = true;
        }
    }

    private void Browser_NavigationCompleted(
        object? sender,
        CoreWebView2NavigationCompletedEventArgs e)
    {
        diagnosticLog.Write(
            "navigation-completed",
            new
            {
                e.IsSuccess,
                webErrorStatus = e.WebErrorStatus.ToString(),
                source = WebViewDiagnosticLog.DescribeUri(Browser.Source?.AbsoluteUri),
                navigationWasBlocked,
            });
        if (navigationWasBlocked)
        {
            navigationWasBlocked = false;
            return;
        }

        if (e.IsSuccess)
        {
            Browser.Visibility = Visibility.Visible;
            StatusPanel.Visibility = Visibility.Collapsed;
            return;
        }

        ShowFailure(
            AppResources.WebContentUnavailable,
            $"{AppResources.WebNavigationFailed} ({e.WebErrorStatus})");
    }

    private void Browser_ProcessFailed(
        object? sender,
        CoreWebView2ProcessFailedEventArgs e)
    {
        diagnosticLog.Write(
            "webview-process-failed",
            new { processFailedKind = e.ProcessFailedKind.ToString() });
        ShowFailure(
            AppResources.WebContentUnavailable,
            $"{AppResources.WebNavigationFailed} ({e.ProcessFailedKind})");
    }

    private async void Browser_WebMessageReceived(
        object? sender,
        CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (navigationPolicy?.IsTrustedMessageSource(e.Source) != true
            || !RestrictedBridgeProtocol.TryParseRequest(e.WebMessageAsJson, out var request)
            || request is null)
        {
            diagnosticLog.Write(
                "bridge-message-rejected",
                new { source = WebViewDiagnosticLog.DescribeUri(e.Source) });
            return;
        }

        diagnosticLog.Write(
            "bridge-message-accepted",
            new { requestType = request.Type.ToString(), request.RequestId });
        try
        {
            switch (request.Type)
            {
                case RestrictedBridgeRequestType.ClientReady:
                    await SendSessionStatusAsync(request.RequestId);
                    break;
                case RestrictedBridgeRequestType.OpenExam:
                    await OpenExamAsync(request.RequestId);
                    break;
                case RestrictedBridgeRequestType.FinishExam:
                    await FinishExamAsync(request.RequestId);
                    break;
            }
        }
        catch (Exception)
        {
            PostBridgeResponse(
                ResponseTypeFor(request.Type),
                request.RequestId,
                "failed",
                AppResources.ExamSessionActionFailed);
        }
    }

    private async Task SendSessionStatusAsync(Guid requestId)
    {
        try
        {
            var response = await AgentClient.SendAsync(
                AgentCommand.GetStatus,
                TimeSpan.FromSeconds(5));
            PostBridgeResponse(
                "sessionStatus",
                requestId,
                response.Success ? ToProtocolState(response.State) : "unavailable",
                response.Success ? null : AppResources.AgentStatusUnavailable);
        }
        catch (Exception)
        {
            PostBridgeResponse(
                "sessionStatus",
                requestId,
                "unavailable",
                AppResources.AgentStatusUnavailable);
        }
    }

    private async Task OpenExamAsync(Guid requestId)
    {
        if (actionInProgress)
        {
            PostBridgeResponse(
                "openExamResult",
                requestId,
                "busy",
                AppResources.ExamSessionActionBusy);
            return;
        }

        actionInProgress = true;
        try
        {
            var status = await AgentClient.SendAsync(
                AgentCommand.GetStatus,
                TimeSpan.FromSeconds(5));
            if (!status.Success || status.State != AgentState.InExam)
            {
                PostBridgeResponse(
                    "openExamResult",
                    requestId,
                    "failed",
                    AppResources.NoActiveExamSession);
                return;
            }

            Process.Start(CreateEdgeStartInfo());
            PostBridgeResponse(
                "openExamResult",
                requestId,
                "opened",
                AppResources.ExamOpened);
        }
        catch (Exception)
        {
            PostBridgeResponse(
                "openExamResult",
                requestId,
                "failed",
                AppResources.EdgeOpenFailed);
        }
        finally
        {
            actionInProgress = false;
        }
    }

    private async Task FinishExamAsync(Guid? requestId)
    {
        if (actionInProgress)
        {
            PostFinishResponse(requestId, "busy", AppResources.ExamSessionActionBusy);
            return;
        }

        actionInProgress = true;
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
                PostFinishResponse(requestId, "cancelled", AppResources.FinishCancelled);
                return;
            }

            var response = dialog.Response;
            var message = response?.Success == true
                ? AppResources.LeavingExam
                : AppResources.ExamFinishFailed;
            PostFinishResponse(
                requestId,
                response?.Success == true ? "accepted" : "failed",
                message);
            if (requestId is null)
            {
                ShowFailure(
                    response?.Success == true ? AppResources.ExamInProgress : AppResources.WebContentUnavailable,
                    message);
            }
        }
        catch (Exception)
        {
            PostFinishResponse(requestId, "failed", AppResources.ExamFinishFailed);
            if (requestId is null)
            {
                ShowFailure(
                    AppResources.WebContentUnavailable,
                    AppResources.ExamFinishFailed);
            }
        }
        finally
        {
            actionInProgress = false;
        }
    }

    private void PostFinishResponse(Guid? requestId, string state, string message)
    {
        if (requestId is { } id)
        {
            PostBridgeResponse("finishExamResult", id, state, message);
        }
    }

    internal static ProcessStartInfo CreateEdgeStartInfo()
    {
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
        startInfo.ArgumentList.Add("--no-first-run");
        startInfo.ArgumentList.Add("--inprivate");
        startInfo.ArgumentList.Add(FixedExamUri.AbsoluteUri);
        return startInfo;
    }

    private void PostBridgeResponse(
        string type,
        Guid requestId,
        string state,
        string? message)
    {
        if (cleanupInProgress || closeAfterCleanup || Browser.CoreWebView2 is not { } core)
        {
            return;
        }

        var response = new BridgeResponse(
            RestrictedBridgeProtocol.Version,
            type,
            requestId,
            state,
            message);
        core.PostWebMessageAsJson(
            JsonSerializer.Serialize(response, BridgeSerializerOptions));
    }

    private static string ResponseTypeFor(RestrictedBridgeRequestType requestType)
    {
        return requestType switch
        {
            RestrictedBridgeRequestType.ClientReady => "sessionStatus",
            RestrictedBridgeRequestType.OpenExam => "openExamResult",
            RestrictedBridgeRequestType.FinishExam => "finishExamResult",
            _ => throw new ArgumentOutOfRangeException(nameof(requestType)),
        };
    }

    private static string ToProtocolState(AgentState state)
    {
        return JsonNamingPolicy.CamelCase.ConvertName(state.ToString());
    }

    private void ShowLoading()
    {
        Browser.Visibility = Visibility.Collapsed;
        StatusPanel.Visibility = Visibility.Visible;
        StatusHeading.Text = AppResources.ExamSessionTitle;
        StatusMessage.Text = AppResources.LoadingWebContent;
        RetryButton.Visibility = Visibility.Collapsed;
        NativeFinishButton.Visibility = Visibility.Collapsed;
    }

    private void ShowFailure(string heading, string details)
    {
        Browser.Visibility = Visibility.Collapsed;
        StatusPanel.Visibility = Visibility.Visible;
        StatusHeading.Text = heading;
        StatusMessage.Text = details;
        RetryButton.Content = AppResources.Retry;
        RetryButton.Visibility = Visibility.Visible;
        NativeFinishButton.Content = AppResources.ExamDone;
        _ = RefreshNativeFinishButtonAsync();
    }

    private async Task RefreshNativeFinishButtonAsync()
    {
        try
        {
            var response = await AgentClient.SendAsync(
                AgentCommand.GetStatus,
                TimeSpan.FromSeconds(5));
            NativeFinishButton.Visibility =
                StatusPanel.Visibility == Visibility.Visible
                && response.Success
                && response.State == AgentState.InExam
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
        catch (Exception)
        {
            NativeFinishButton.Visibility = Visibility.Collapsed;
        }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (closeAfterCleanup)
        {
            return;
        }

        e.Cancel = true;
        if (cleanupInProgress)
        {
            return;
        }

        if (closeCheckInProgress)
        {
            return;
        }

        closeCheckInProgress = true;
        if (await IsExamActiveAsync())
        {
            NativeFinishButton.Content = AppResources.ExamDone;
            NativeFinishButton.Visibility = Visibility.Visible;
            closeCheckInProgress = false;
            return;
        }

        cleanupInProgress = true;
        closeCheckInProgress = false;

        try
        {
            Browser.Dispose();
            await DeleteProfileDirectoryWithRetryAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"{AppResources.ProfileCleanupFailed}{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                AppResources.ExamSessionTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        closeAfterCleanup = true;
        Close();
    }

    private static async Task<bool> IsExamActiveAsync()
    {
        try
        {
            var response = await AgentClient.SendAsync(
                AgentCommand.GetStatus,
                TimeSpan.FromSeconds(5));
            return !response.Success || response.State == AgentState.InExam;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private async Task DeleteProfileDirectoryWithRetryAsync()
    {
        const int maximumAttempts = 5;
        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            try
            {
                DeleteProfileDirectory();
                return;
            }
            catch (IOException) when (attempt < maximumAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt));
            }
            catch (UnauthorizedAccessException) when (attempt < maximumAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt));
            }
        }
    }

    private void DeleteProfileDirectory()
    {
        if (Directory.Exists(profilePath))
        {
            Directory.Delete(profilePath, recursive: true);
        }
    }

    private sealed record BridgeResponse(
        int Version,
        string Type,
        Guid RequestId,
        string State,
        string? Message);
}
