using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ExamKiosk.Contracts;
using ExamKiosk.DeviceAgent.WindowsConfiguration;
using ExamKiosk.DeviceAgent.WindowsConfiguration.Models;

namespace ExamKiosk.DeviceAgent;

public sealed class TransitionManager
{
    private const string ProfileId = "{9A2A490F-10F6-4764-974A-43B19E722C23}";
    private readonly SemaphoreSlim transitionLock = new(1, 1);
    private readonly ILogger<TransitionManager> logger;
    private readonly string configurationDirectory;
    private readonly string statePath;
    private readonly SessionJournal sessionJournal;
    private readonly Func<
        string,
        IReadOnlyList<string>,
        CancellationToken,
        Task<string>>? scriptRunner;
    private readonly Func<DateTimeOffset> restartScheduler;
    private readonly WindowsConfigurationCompiler windowsConfigurationCompiler;
    private readonly Func<WindowsClientVersion> windowsClientVersionProvider;
    private bool initialized;

    public TransitionManager(
        ILogger<TransitionManager> logger,
        WindowsConfigurationCompiler windowsConfigurationCompiler)
        : this(
            logger,
            GetDataDirectory(),
            null,
            null,
            Path.Combine(AppContext.BaseDirectory, "Configuration"),
            windowsConfigurationCompiler)
    {
    }

    internal TransitionManager(
        ILogger<TransitionManager> logger,
        string dataDirectory,
        Func<
            string,
            IReadOnlyList<string>,
            CancellationToken,
            Task<string>>? scriptRunner,
        Func<DateTimeOffset>? restartScheduler,
        string? configurationDirectory = null,
        WindowsConfigurationCompiler? windowsConfigurationCompiler = null,
        Func<WindowsClientVersion>? windowsClientVersionProvider = null)
    {
        this.logger = logger;
        this.scriptRunner = scriptRunner;
        this.restartScheduler = restartScheduler ?? ScheduleRestart;
        this.windowsConfigurationCompiler =
            windowsConfigurationCompiler ?? new WindowsConfigurationCompiler();
        this.windowsClientVersionProvider =
            windowsClientVersionProvider ?? GetCurrentWindowsClientVersion;
        this.configurationDirectory = configurationDirectory
            ?? Path.Combine(dataDirectory, "Configuration");
        Directory.CreateDirectory(dataDirectory);
        statePath = Path.Combine(dataDirectory, "agent-state.json");
        sessionJournal = new SessionJournal(Path.Combine(dataDirectory, "session-journal.json"));
        CurrentState = ReadState();
    }

    public AgentState CurrentState { get; private set; }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await transitionLock.WaitAsync(cancellationToken);
        try
        {
            if (initialized)
            {
                return;
            }

            try
            {
                var examModeConfigured = await IsExamModeConfiguredAsync(cancellationToken);
                if (examModeConfigured)
                {
                    await sessionJournal.SetStateAsync(AgentState.InExam, cancellationToken);
                    await sessionJournal.RecordStepAsync(
                        "AssignedAccessDetected",
                        "completed",
                        null,
                        cancellationToken);
                    await RunCustomizationScriptAsync(
                        "OnExamStart.ps1",
                        cancellationToken);
                    await sessionJournal.RecordStepAsync(
                        "OnExamStart",
                        "completed",
                        null,
                        cancellationToken);
                }

                var reconciledState = ReconcileState(CurrentState, examModeConfigured);
                if (reconciledState != CurrentState)
                {
                    logger.LogWarning(
                        "Reconciled persisted agent state {PersistedState} to {ReconciledState}",
                        CurrentState,
                        reconciledState);
                    await SetStateAsync(reconciledState, cancellationToken);
                }

                initialized = true;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Assigned Access state reconciliation failed");
                await SetStateAsync(AgentState.Failed, CancellationToken.None);
                initialized = true;
            }
        }
        finally
        {
            transitionLock.Release();
        }
    }

    public async Task<AgentResponse> HandleAsync(
        AgentRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Command == AgentCommand.GetStatus)
        {
            return Success(request, $"The agent state is {CurrentState}.");
        }

        await transitionLock.WaitAsync(cancellationToken);
        try
        {
            return request.Command switch
            {
                AgentCommand.StartExam => await StartExamAsync(request, cancellationToken),
                AgentCommand.FinishExam => await FinishExamAsync(request, cancellationToken),
                _ => Failure(request, "The requested command is not supported.")
            };
        }
        finally
        {
            transitionLock.Release();
        }
    }

    private async Task<AgentResponse> StartExamAsync(
        AgentRequest request,
        CancellationToken cancellationToken)
    {
        if (request.StartExam is not { } startExam)
        {
            return Failure(request, "The StartExam payload is required.");
        }
        if (!CanStartExam(CurrentState))
        {
            return Failure(request, $"An exam cannot start while the agent state is {CurrentState}.");
        }

        await SetStateAsync(AgentState.EnteringExam, cancellationToken);
        try
        {
            var profileSha256 = EffectiveProfileDigest.Compute(
                startExam.Profile);
            await sessionJournal.BeginAsync(
                startExam.SessionId,
                profileSha256,
                cancellationToken);
            await sessionJournal.RecordStepAsync(
                "ProfileReceived",
                "completed",
                null,
                cancellationToken);
            EffectiveWindowsConfiguration windowsConfiguration;
            try
            {
                windowsConfiguration = windowsConfigurationCompiler.Compile(
                    windowsClientVersionProvider(),
                    startExam.Profile);
                await sessionJournal.SetAssignedAccessSha256Async(
                    windowsConfiguration.AssignedAccess.Sha256,
                    cancellationToken);
                await sessionJournal.RecordStepAsync(
                    "WindowsConfigurationGenerated",
                    "completed",
                    null,
                    cancellationToken);
            }
            catch (Exception exception)
                when (exception is WindowsConfigurationException
                    or NotSupportedException)
            {
                logger.LogWarning(
                    exception,
                    "Could not generate a valid Windows configuration");
                await sessionJournal.RecordStepAsync(
                    "WindowsConfigurationGenerated",
                    "failed",
                    exception.Message,
                    cancellationToken);
                await sessionJournal.SetStateAsync(
                    AgentState.Available,
                    cancellationToken);
                await SetStateAsync(
                    AgentState.Available,
                    cancellationToken);
                return Failure(
                    request,
                    $"The Windows configuration could not be generated: {exception.Message}");
            }

            await WriteGeneratedAssignedAccessPreviewAsync(
                windowsConfiguration.AssignedAccess,
                cancellationToken);
            await sessionJournal.RecordStepAsync(
                "GeneratedAssignedAccessPreview",
                "completed",
                null,
                cancellationToken);
            await sessionJournal.RecordStepAsync(
                "AssignedAccessApply",
                "started",
                null,
                cancellationToken);
            var configurationPath = Path.Combine(
                configurationDirectory,
                "AssignedAccess.xml");
            // Temporarily disabled while comparing fixed and generated Assigned Access XML.
            // await RunPowerShellAsync(
            //     "Start-Exam.ps1",
            //     ["-ConfigurationPath", configurationPath],
            //     cancellationToken);
            if (!await IsExamModeConfiguredAsync(cancellationToken))
            {
                throw new InvalidOperationException(
                    "Assigned Access verification did not find the expected Exam Kiosk profile.");
            }
            await sessionJournal.RecordStepAsync(
                "AssignedAccessApply",
                "completed",
                null,
                cancellationToken);
            var restartAtUtc = restartScheduler();
            await sessionJournal.RecordStepAsync(
                "Restart",
                "scheduled",
                null,
                cancellationToken);
            return Success(
                request,
                "Assigned Access was applied. Windows will restart shortly.",
                restartAtUtc);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to enter exam mode");
            await TryRecordJournalStepAsync(
                "AssignedAccessApply",
                "failed",
                exception.Message);

            var recoveryException = await TryRecoverFailedStartAsync();
            if (recoveryException is null)
            {
                return Failure(
                    request,
                    $"Exam mode could not be applied and was removed safely: {exception.Message}");
            }

            return Failure(
                request,
                "Exam mode could not be applied and automatic cleanup failed. "
                + $"Manual recovery is required: {recoveryException.Message}");
        }
    }

    private async Task<Exception?> TryRecoverFailedStartAsync()
    {
        await TryRecordJournalStepAsync(
            "AssignedAccessRecovery",
            "started",
            null);

        try
        {
            if (await IsExamModeConfiguredAsync(CancellationToken.None))
            {
                await RunPowerShellAsync(
                    "Stop-Exam.ps1",
                    [],
                    CancellationToken.None);
                if (await IsExamModeConfiguredAsync(CancellationToken.None))
                {
                    throw new InvalidOperationException(
                        "The expected Exam Kiosk Assigned Access profile remained configured after cleanup.");
                }
            }

            await sessionJournal.SetStateAsync(
                AgentState.Available,
                CancellationToken.None);
            await TryRecordJournalStepAsync(
                "AssignedAccessRecovery",
                "completed",
                null);
            await SetStateAsync(AgentState.Available, CancellationToken.None);
            return null;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Automatic cleanup after an Assigned Access apply failure failed");
            await TryRecordJournalStepAsync(
                "AssignedAccessRecovery",
                "failed",
                exception.Message);
            await TrySetJournalStateAsync(AgentState.Failed);
            await SetStateAsync(AgentState.Failed, CancellationToken.None);
            return exception;
        }
    }

    private async Task TryRecordJournalStepAsync(
        string name,
        string status,
        string? error)
    {
        try
        {
            await sessionJournal.RecordStepAsync(
                name,
                status,
                error,
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to record session journal step {StepName} with status {StepStatus}",
                name,
                status);
        }
    }

    private async Task TrySetJournalStateAsync(AgentState state)
    {
        try
        {
            await sessionJournal.SetStateAsync(state, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to set session journal state to {State}",
                state);
        }
    }

    private async Task<AgentResponse> FinishExamAsync(
        AgentRequest request,
        CancellationToken cancellationToken)
    {
        if (CurrentState != AgentState.InExam)
        {
            return Failure(request, $"The exam cannot finish while the agent state is {CurrentState}.");
        }

        await SetStateAsync(AgentState.ExitingExam, cancellationToken);
        try
        {
            await sessionJournal.SetStateAsync(AgentState.ExitingExam, cancellationToken);
            await sessionJournal.RecordStepAsync(
                "OnExamEnd",
                "started",
                null,
                cancellationToken);
            await RunCustomizationScriptAsync("OnExamEnd.ps1", cancellationToken);
            await sessionJournal.RecordStepAsync(
                "OnExamEnd",
                "completed",
                null,
                cancellationToken);
            await sessionJournal.RecordStepAsync(
                "AssignedAccessRemove",
                "started",
                null,
                cancellationToken);
            await RunPowerShellAsync("Stop-Exam.ps1", [], cancellationToken);
            await sessionJournal.RecordStepAsync(
                "AssignedAccessRemove",
                "completed",
                null,
                cancellationToken);
            await sessionJournal.SetStateAsync(AgentState.Available, cancellationToken);
            var restartAtUtc = ScheduleRestart();
            await sessionJournal.RecordStepAsync(
                "Restart",
                "scheduled",
                null,
                cancellationToken);
            return Success(
                request,
                "Assigned Access was removed. Windows will restart shortly.",
                restartAtUtc);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to leave exam mode");
            await SetStateAsync(AgentState.Failed, CancellationToken.None);
            return Failure(request, $"Exam mode could not be removed: {exception.Message}");
        }
    }

    internal static AgentState ReconcileState(
        AgentState persistedState,
        bool examModeConfigured)
    {
        _ = persistedState;
        return examModeConfigured ? AgentState.InExam : AgentState.Available;
    }

    internal static bool CanStartExam(AgentState state) =>
        state is AgentState.Available or AgentState.InExam;

    private async Task WriteGeneratedAssignedAccessPreviewAsync(
        AssignedAccessArtifact assignedAccess,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assignedAccess);
        Directory.CreateDirectory(configurationDirectory);
        var previewPath = Path.Combine(
            configurationDirectory,
            "AssignedAccess.generated.temp.xml");
        var temporaryPath = previewPath + ".tmp";
        await File.WriteAllTextAsync(
            temporaryPath,
            assignedAccess.Xml,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
        File.Move(temporaryPath, previewPath, overwrite: true);
        logger.LogInformation(
            "Wrote generated Assigned Access preview {PreviewPath} with declared SHA-256 {Sha256}",
            previewPath,
            assignedAccess.Sha256);
    }

    private static WindowsClientVersion GetCurrentWindowsClientVersion()
    {
        var version = Environment.OSVersion.Version;
        return new WindowsClientVersion(
            version.Major,
            version.Minor,
            version.Build);
    }

    private async Task<bool> IsExamModeConfiguredAsync(CancellationToken cancellationToken)
    {
        var output = await RunPowerShellAsync(
            "Get-ExamMode.ps1",
            ["-ExpectedProfileId", ProfileId],
            cancellationToken);

        return output.Trim() switch
        {
            "Configured" => true,
            "NotConfigured" => false,
            "ForeignConfiguration" => throw new InvalidOperationException(
                "Assigned Access is configured by another profile. The Exam Kiosk PoC will not replace it."),
            _ => throw new InvalidDataException(
                $"Get-ExamMode.ps1 returned an unexpected result: {output.Trim()}")
        };
    }

    private async Task<string> RunPowerShellAsync(
        string scriptName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        if (scriptRunner is not null)
        {
            return await scriptRunner(scriptName, arguments, cancellationToken);
        }

        var scriptPath = Path.Combine(AppContext.BaseDirectory, "Scripts", scriptName);
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("A required agent script was not found.", scriptPath);
        }

        return await RunPowerShellFileAsync(
            scriptPath,
            scriptName,
            arguments,
            cancellationToken);
    }

    private async Task<string> RunPowerShellFileAsync(
        string scriptPath,
        string scriptName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {

        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32",
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe"),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("PowerShell could not be started.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var output = await standardOutput;
        var error = await standardError;
        logger.LogInformation("{ScriptName} output: {Output}", scriptName, output.Trim());

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{scriptName} exited with code {process.ExitCode}: {error.Trim()}");
        }

        return output;
    }

    private async Task RunCustomizationScriptAsync(
        string scriptName,
        CancellationToken cancellationToken)
    {
        var scriptPath = Path.Combine(
            AppContext.BaseDirectory,
            "Customization",
            scriptName);
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException(
                "An exam customization script was not found.",
                scriptPath);
        }

        await RunPowerShellFileAsync(
            scriptPath,
            scriptName,
            [],
            cancellationToken);
    }

    private DateTimeOffset ScheduleRestart()
    {
        var restartAtUtc = DateTimeOffset.UtcNow.AddSeconds(RestartSchedule.DelaySeconds);
        // Temporarily disabled while comparing fixed and generated Assigned Access XML.
        // _ = RestartAtAsync(restartAtUtc);
        return restartAtUtc;
    }

    private static string GetDataDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ExamKiosk");

    private async Task RestartAtAsync(DateTimeOffset restartAtUtc)
    {
        try
        {
            var delay = restartAtUtc - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay);
            }

            using var process = Process.Start(CreateRestartStartInfo())
                ?? throw new InvalidOperationException("The Windows restart command could not be started.");
            logger.LogInformation("Windows restart initiated after the Exam Kiosk countdown");
        }
        catch (Exception exception)
        {
            logger.LogCritical(exception, "Windows restart failed after the Exam Kiosk countdown");
        }
    }

    internal static ProcessStartInfo CreateRestartStartInfo()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "shutdown.exe"),
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("/r");
        startInfo.ArgumentList.Add("/t");
        startInfo.ArgumentList.Add("0");
        startInfo.ArgumentList.Add("/f");
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("p:4:1");
        return startInfo;
    }

    private async Task SetStateAsync(AgentState state, CancellationToken cancellationToken)
    {
        CurrentState = state;
        var temporaryPath = statePath + ".tmp";
        var json = JsonSerializer.Serialize(
            new PersistedAgentState(state, DateTimeOffset.UtcNow),
            AgentProtocol.SerializerOptions);
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        File.Move(temporaryPath, statePath, true);
        logger.LogInformation("Agent state changed to {State}", state);
    }

    private AgentState ReadState()
    {
        try
        {
            if (!File.Exists(statePath))
            {
                return AgentState.Available;
            }

            var persisted = JsonSerializer.Deserialize<PersistedAgentState>(
                File.ReadAllText(statePath),
                AgentProtocol.SerializerOptions);
            return persisted?.State ?? AgentState.Failed;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The persisted agent state could not be read");
            return AgentState.Failed;
        }
    }

    private AgentResponse Success(
        AgentRequest request,
        string message,
        DateTimeOffset? restartAtUtc = null) =>
        new(
            AgentProtocol.Version,
            request.RequestId,
            true,
            CurrentState,
            message,
            restartAtUtc);

    private AgentResponse Failure(AgentRequest request, string message) =>
        new(AgentProtocol.Version, request.RequestId, false, CurrentState, message);

    private sealed record PersistedAgentState(AgentState State, DateTimeOffset UpdatedAt);
}