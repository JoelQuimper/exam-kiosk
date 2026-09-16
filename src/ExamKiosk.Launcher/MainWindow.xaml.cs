using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using ExamKiosk.Contracts;
using Microsoft.Web.WebView2.Core;
using AppResources = ExamKiosk.Contracts.Resources;

namespace ExamKiosk.Launcher;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions BridgeSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string profilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ExamKiosk",
        "LauncherWebView2");
    private readonly WebViewDiagnosticLog diagnosticLog = new("launcher");
    private WebViewHostConfiguration? configuration;
    private WebViewNavigationPolicy? navigationPolicy;
    private bool initializationInProgress;
    private bool startInProgress;
    private bool cleanupInProgress;
    private bool closeAfterCleanup;
    private bool navigationWasBlocked;

    public MainWindow()
    {
        InitializeComponent();
        Title = AppResources.KioskTitle;
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
                "launcher.settings.json",
                "/launcher");
            diagnosticLog.Write(
                "configuration-loaded",
                new { page = WebViewDiagnosticLog.DescribeUri(configuration.PageUri.AbsoluteUri) });
            navigationPolicy ??= new WebViewNavigationPolicy(
                configuration,
                [new Uri("https://login.microsoftonline.com")]);

            if (Browser.CoreWebView2 is null)
            {
                DeleteProfileDirectory();
                var environmentOptions = new CoreWebView2EnvironmentOptions
                {
                    AreBrowserExtensionsEnabled = false,
                };
                var environment = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: profilePath,
                    options: environmentOptions);
                await Browser.EnsureCoreWebView2Async(environment);
                ConfigureBrowser();
                diagnosticLog.Write("webview-configured");
            }

            var core = Browser.CoreWebView2
                ?? throw new InvalidOperationException("WebView2 initialization did not complete.");
            core.Navigate(configuration.PageUri.AbsoluteUri);
        }
        catch (Exception exception)
        {
            diagnosticLog.Write(
                "initialization-failed",
                new { exceptionType = exception.GetType().FullName, exception.Message });
            var details = exception is InvalidDataException
                ? AppResources.LauncherConfigurationInvalid
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
            || !LauncherBridgeProtocol.TryParseRequest(e.WebMessageAsJson, out var request)
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
                case LauncherBridgeRequestType.ClientReady:
                    await SendAgentStatusAsync(request.RequestId);
                    break;
                case LauncherBridgeRequestType.StartExam:
                    await StartExamAsync(request.RequestId);
                    break;
            }
        }
        catch (Exception)
        {
            ShowFailure(AppResources.WebContentUnavailable, AppResources.WebNavigationFailed);
            if (request.Type == LauncherBridgeRequestType.ClientReady)
            {
                PostBridgeResponse(
                    "agentStatus",
                    request.RequestId,
                    "unavailable",
                    AppResources.AgentStatusUnavailable);
            }
            else
            {
                PostBridgeResponse(
                    "startExamResult",
                    request.RequestId,
                    "failed",
                    AppResources.PreparationFailed);
            }
        }
    }

    private async Task SendAgentStatusAsync(Guid requestId)
    {
        try
        {
            var response = await AgentClient.SendAsync(
                AgentCommand.GetStatus,
                TimeSpan.FromSeconds(5));
            PostBridgeResponse(
                "agentStatus",
                requestId,
                response.Success ? ToProtocolState(response.State) : "unavailable",
                response.Success ? null : AppResources.AgentStatusUnavailable);
        }
        catch (Exception)
        {
            PostBridgeResponse(
                "agentStatus",
                requestId,
                "unavailable",
                AppResources.AgentStatusUnavailable);
        }
    }

    private async Task StartExamAsync(Guid requestId)
    {
        if (startInProgress)
        {
            PostBridgeResponse(
                "startExamResult",
                requestId,
                "busy",
                AppResources.PreparingDevice);
            return;
        }

        startInProgress = true;
        try
        {
            if (MessageBox.Show(
                    this,
                    AppResources.StartWarning,
                    AppResources.StartWarningTitle,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                PostBridgeResponse(
                    "startExamResult",
                    requestId,
                    "cancelled",
                    AppResources.StartCancelled);
                return;
            }

            var response = await AgentClient.SendAsync(
                AgentCommand.StartExam,
                TimeSpan.FromSeconds(30));
            PostBridgeResponse(
                "startExamResult",
                requestId,
                response.Success ? "accepted" : "failed",
                response.Success ? AppResources.PreparingDevice : AppResources.PreparationFailed);
            if (response.Success && response.RestartAtUtc is { } restartAtUtc)
            {
                new RestartCountdownWindow(this, restartAtUtc).ShowDialog();
            }
        }
        catch (Exception)
        {
            PostBridgeResponse(
                "startExamResult",
                requestId,
                "failed",
                AppResources.PreparationFailed);
        }
        finally
        {
            startInProgress = false;
        }
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
            LauncherBridgeProtocol.Version,
            type,
            requestId,
            state,
            message);
        core.PostWebMessageAsJson(
            JsonSerializer.Serialize(response, BridgeSerializerOptions));
    }

    private static string ToProtocolState(AgentState state)
    {
        return JsonNamingPolicy.CamelCase.ConvertName(state.ToString());
    }

    private void ShowLoading()
    {
        Browser.Visibility = Visibility.Collapsed;
        StatusPanel.Visibility = Visibility.Visible;
        StatusHeading.Text = AppResources.KioskTitle;
        StatusMessage.Text = AppResources.LoadingWebContent;
        RetryButton.Visibility = Visibility.Collapsed;
    }

    private void ShowFailure(string heading, string details)
    {
        Browser.Visibility = Visibility.Collapsed;
        StatusPanel.Visibility = Visibility.Visible;
        StatusHeading.Text = heading;
        StatusMessage.Text = details;
        RetryButton.Content = AppResources.Retry;
        RetryButton.Visibility = Visibility.Visible;
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

        cleanupInProgress = true;
        Browser.Dispose();

        try
        {
            await DeleteProfileDirectoryWithRetryAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"{AppResources.ProfileCleanupFailed}{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                AppResources.KioskTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        closeAfterCleanup = true;
        Close();
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
