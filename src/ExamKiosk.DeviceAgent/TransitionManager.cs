using System.Diagnostics;
using System.Text.Json;
using ExamKiosk.Contracts;

namespace ExamKiosk.DeviceAgent;

public sealed class TransitionManager
{
    private const string ProfileId = "{9A2A490F-10F6-4764-974A-43B19E722C23}";
    private readonly SemaphoreSlim transitionLock = new(1, 1);
    private readonly ILogger<TransitionManager> logger;
    private readonly string statePath;
    private readonly SessionJournal sessionJournal;
    private bool initialized;

    public TransitionManager(ILogger<TransitionManager> logger)
    {
        this.logger = logger;
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ExamKiosk");
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
        if (!CanStartExam(CurrentState))
        {
            return Failure(request, $"An exam cannot start while the agent state is {CurrentState}.");
        }

        await SetStateAsync(AgentState.EnteringExam, cancellationToken);
        try
        {
            await sessionJournal.BeginAsync(cancellationToken);
            await sessionJournal.RecordStepAsync(
                "AssignedAccessApply",
                "started",
                null,
                cancellationToken);
            var configurationPath = Path.Combine(
                AppContext.BaseDirectory,
                "Configuration",
                "AssignedAccess.xml");
            await RunPowerShellAsync(
                "Start-Exam.ps1",
                ["-ConfigurationPath", configurationPath],
                cancellationToken);
            await sessionJournal.RecordStepAsync(
                "AssignedAccessApply",
                "completed",
                null,
                cancellationToken);
            var restartAtUtc = ScheduleRestart("Entering the restricted exam session");
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
            await SetStateAsync(AgentState.Failed, CancellationToken.None);
            return Failure(request, $"Exam mode could not be applied: {exception.Message}");
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
            var restartAtUtc = ScheduleRestart("Leaving the restricted exam session");
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

    private DateTimeOffset ScheduleRestart(string reason)
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
        startInfo.ArgumentList.Add(RestartSchedule.DelaySeconds.ToString());
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("p:4:1");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(reason);
        Process.Start(startInfo);
        return DateTimeOffset.UtcNow.AddSeconds(RestartSchedule.DelaySeconds);
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