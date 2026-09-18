using System.Security.Cryptography;
using System.Text.Json;
using ExamKiosk.Contracts;
using ExamKiosk.DeviceAgent.WindowsConfiguration.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamKiosk.DeviceAgent.Tests;

public sealed class TransitionManagerTests
{
    [Fact]
    public async Task StartExam_WhenApplyFailsWithoutConfiguration_ReturnsAvailable()
    {
        using var directory = new TemporaryDirectory();
        var scripts = new ScriptSequence(
            ("Start-Exam.ps1", null, new InvalidOperationException("apply failed")),
            ("Get-ExamMode.ps1", "NotConfigured", null));
        var restartScheduled = false;
        var manager = CreateManager(
            directory.Path,
            scripts,
            () =>
            {
                restartScheduled = true;
                return DateTimeOffset.UtcNow;
            });

        var response = await manager.HandleAsync(StartRequest(), CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(AgentState.Available, response.State);
        Assert.Contains("removed safely", response.Message);
        Assert.False(restartScheduled);
        Assert.Equal(
            ["Start-Exam.ps1", "Get-ExamMode.ps1"],
            scripts.Calls);
        AssertRecoveryJournal(directory.Path, "completed", AgentState.Available);
    }

    [Fact]
    public async Task StartExam_WhenExpectedProfileWasPartiallyApplied_RemovesAndVerifiesIt()
    {
        using var directory = new TemporaryDirectory();
        var scripts = new ScriptSequence(
            ("Start-Exam.ps1", null, new InvalidOperationException("apply failed")),
            ("Get-ExamMode.ps1", "Configured", null),
            ("Stop-Exam.ps1", "removed", null),
            ("Get-ExamMode.ps1", "NotConfigured", null));
        var manager = CreateManager(directory.Path, scripts);

        var response = await manager.HandleAsync(StartRequest(), CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(AgentState.Available, response.State);
        Assert.Equal(
            [
                "Start-Exam.ps1",
                "Get-ExamMode.ps1",
                "Stop-Exam.ps1",
                "Get-ExamMode.ps1",
            ],
            scripts.Calls);
        AssertRecoveryJournal(directory.Path, "completed", AgentState.Available);
    }

    [Fact]
    public async Task StartExam_WhenRecoveryFindsForeignConfiguration_DoesNotRemoveIt()
    {
        using var directory = new TemporaryDirectory();
        var scripts = new ScriptSequence(
            ("Start-Exam.ps1", null, new InvalidOperationException("apply failed")),
            ("Get-ExamMode.ps1", "ForeignConfiguration", null));
        var manager = CreateManager(directory.Path, scripts);

        var response = await manager.HandleAsync(StartRequest(), CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(AgentState.Failed, response.State);
        Assert.Contains("Manual recovery is required", response.Message);
        Assert.DoesNotContain("Stop-Exam.ps1", scripts.Calls);
        AssertRecoveryJournal(directory.Path, "failed", AgentState.Failed);
    }

    [Fact]
    public async Task StartExam_WhenCleanupFails_RemainsFailed()
    {
        using var directory = new TemporaryDirectory();
        var scripts = new ScriptSequence(
            ("Start-Exam.ps1", null, new InvalidOperationException("apply failed")),
            ("Get-ExamMode.ps1", "Configured", null),
            ("Stop-Exam.ps1", null, new InvalidOperationException("cleanup failed")));
        var manager = CreateManager(directory.Path, scripts);

        var response = await manager.HandleAsync(StartRequest(), CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(AgentState.Failed, response.State);
        Assert.Contains("cleanup failed", response.Message);
        AssertRecoveryJournal(directory.Path, "failed", AgentState.Failed);
    }

    [Fact]
    public async Task StartExam_VerifiesExpectedProfileBeforeSchedulingRestart()
    {
        using var directory = new TemporaryDirectory();
        var scripts = new ScriptSequence(
            ("Start-Exam.ps1", "applied", null),
            ("Get-ExamMode.ps1", "Configured", null));
        var restartAtUtc = DateTimeOffset.UtcNow.AddSeconds(5);
        var manager = CreateManager(
            directory.Path,
            scripts,
            () => restartAtUtc);

        var response = await manager.HandleAsync(StartRequest(), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(restartAtUtc, response.RestartAtUtc);
        Assert.Equal(
            ["Start-Exam.ps1", "Get-ExamMode.ps1"],
            scripts.Calls);
    }

    [Fact]
    public async Task StartExam_WritesEveryDeclaredWebToolToManifestBeforeApply()
    {
        using var directory = new TemporaryDirectory();
        var scripts = new ScriptSequence(
            ("Start-Exam.ps1", "applied", null),
            ("Get-ExamMode.ps1", "Configured", null));
        var manager = CreateManager(directory.Path, scripts);
        ToolDefinition[] tools =
        [
            WebTool("dictionary", "Dictionary", "https://dictionary.example/"),
            WebTool("reference", "Reference", "https://reference.example/"),
        ];

        var response = await manager.HandleAsync(
            StartRequest(tools),
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(
            ["Start-Exam.ps1", "Get-ExamMode.ps1"],
            scripts.Calls);
        var manifestPath = Path.Combine(
            directory.Path,
            "Configuration",
            "WebShortcuts.generated.temp.json");
        using var manifest = JsonDocument.Parse(
            await File.ReadAllTextAsync(manifestPath));
        var entries = manifest.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, entries.Length);
        Assert.Equal(
            @"%ALLUSERSPROFILE%\Microsoft\Windows\Start Menu\Programs\Exam Kiosk\tool-dictionary.lnk",
            entries[0].GetProperty("linkPath").GetString());
        Assert.Equal(
            "https://dictionary.example/",
            entries[0].GetProperty("entryUrl").GetString());
        Assert.Equal(
            manifestPath,
            scripts.Invocations[0].Arguments[3]);
        Assert.Equal(
            "GeneratedWebShortcutsManifest",
            new SessionJournal(
                Path.Combine(directory.Path, "session-journal.json"))
                .Current!
                .Steps[3]
                .Name);
    }

    [Fact]
    public async Task StartExam_PersistsSessionAndProfileReceiptBeforeApply()
    {
        using var directory = new TemporaryDirectory();
        var scripts = new ScriptSequence(
            ("Start-Exam.ps1", "applied", null),
            ("Get-ExamMode.ps1", "Configured", null));
        var manager = CreateManager(directory.Path, scripts);
        var request = StartRequest();

        var response = await manager.HandleAsync(
            request,
            CancellationToken.None);

        var journal = new SessionJournal(
            Path.Combine(directory.Path, "session-journal.json")).Current;
        Assert.True(response.Success);
        Assert.NotNull(journal);
        Assert.Equal(request.StartExam!.SessionId, journal.SessionId);
        Assert.Equal(
            EffectiveProfileDigest.Compute(request.StartExam.Profile),
            journal.ProfileSha256);
        Assert.Equal("ProfileReceived", journal.Steps[0].Name);
        Assert.Equal("completed", journal.Steps[0].Status);
        Assert.Equal("WindowsConfigurationGenerated", journal.Steps[1].Name);
        Assert.Equal("GeneratedAssignedAccessPreview", journal.Steps[2].Name);
        Assert.Equal("AssignedAccessApply", journal.Steps[3].Name);
        var previewPath = Path.Combine(
            directory.Path,
            "Configuration",
            "AssignedAccess.generated.temp.xml");
        Assert.Equal(
            journal.AssignedAccessSha256,
            Convert.ToHexStringLower(
                SHA256.HashData(await File.ReadAllBytesAsync(previewPath))));
        Assert.False(
            File.Exists(
                previewPath + ".tmp"));
    }

    [Fact]
    public async Task StartExam_WhenReadBackVerificationFails_DoesNotScheduleRestart()
    {
        using var directory = new TemporaryDirectory();
        var scripts = new ScriptSequence(
            ("Start-Exam.ps1", "applied", null),
            ("Get-ExamMode.ps1", "NotConfigured", null),
            ("Get-ExamMode.ps1", "NotConfigured", null));
        var restartScheduled = false;
        var manager = CreateManager(
            directory.Path,
            scripts,
            () =>
            {
                restartScheduled = true;
                return DateTimeOffset.UtcNow;
            });

        var response = await manager.HandleAsync(StartRequest(), CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(AgentState.Available, response.State);
        Assert.False(restartScheduled);
        Assert.Equal(
            [
                "Start-Exam.ps1",
                "Get-ExamMode.ps1",
                "Get-ExamMode.ps1",
            ],
            scripts.Calls);
        AssertRecoveryJournal(directory.Path, "completed", AgentState.Available);
    }

    [Fact]
    public void RestartSchedule_UsesFiveSecondDelay()
    {
        Assert.Equal(5, RestartSchedule.DelaySeconds);
    }

    [Fact]
    public void RestartCommand_IsImmediateAndForcedAfterApplicationCountdown()
    {
        var startInfo = TransitionManager.CreateRestartStartInfo();

        Assert.EndsWith(
            Path.Combine("System32", "shutdown.exe"),
            startInfo.FileName,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            ["/r", "/t", "0", "/f", "/d", "p:4:1"],
            startInfo.ArgumentList);
    }

    [Theory]
    [InlineData(5.0, 5)]
    [InlineData(4.1, 5)]
    [InlineData(4.0, 4)]
    [InlineData(-1.0, 0)]
    public void RestartSchedule_ReturnsVisibleWholeSeconds(
        double secondsUntilRestart,
        int expected)
    {
        var now = DateTimeOffset.UtcNow;

        var result = RestartSchedule.GetRemainingSeconds(
            now.AddSeconds(secondsUntilRestart),
            now);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(AgentState.Available, true)]
    [InlineData(AgentState.EnteringExam, false)]
    [InlineData(AgentState.InExam, true)]
    [InlineData(AgentState.ExitingExam, false)]
    [InlineData(AgentState.Failed, false)]
    public void CanStartExam_ReturnsExpectedResult(AgentState state, bool expected)
    {
        var result = TransitionManager.CanStartExam(state);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(AgentState.Available)]
    [InlineData(AgentState.EnteringExam)]
    [InlineData(AgentState.InExam)]
    [InlineData(AgentState.ExitingExam)]
    [InlineData(AgentState.Failed)]
    public void ReconcileState_WhenAssignedAccessIsConfigured_ReturnsInExam(
        AgentState persistedState)
    {
        var result = TransitionManager.ReconcileState(persistedState, true);

        Assert.Equal(AgentState.InExam, result);
    }

    [Theory]
    [InlineData(AgentState.Available)]
    [InlineData(AgentState.EnteringExam)]
    [InlineData(AgentState.InExam)]
    [InlineData(AgentState.ExitingExam)]
    [InlineData(AgentState.Failed)]
    public void ReconcileState_WhenAssignedAccessIsNotConfigured_ReturnsAvailable(
        AgentState persistedState)
    {
        var result = TransitionManager.ReconcileState(persistedState, false);

        Assert.Equal(AgentState.Available, result);
    }

    private static TransitionManager CreateManager(
        string dataDirectory,
        ScriptSequence scripts,
        Func<DateTimeOffset>? restartScheduler = null) =>
        new(
            NullLogger<TransitionManager>.Instance,
            dataDirectory,
            scripts.RunAsync,
            restartScheduler ?? (() => DateTimeOffset.UtcNow.AddSeconds(5)),
            windowsClientVersionProvider: () => new WindowsClientVersion(10, 0, 22621));

    private static AgentRequest StartRequest(
        IReadOnlyList<ToolDefinition>? tools = null) =>
        new(
            AgentProtocol.Version,
            Guid.NewGuid(),
            AgentCommand.StartExam,
            new AgentStartExamPayload(
                Guid.NewGuid(),
                EffectiveProfileTestData.Create(tools)));

    private static WebToolDefinition WebTool(
        string toolId,
        string label,
        string entryUrl) =>
        new(
            toolId,
            label,
            "web",
            true,
            new WebToolConfiguration(
                [entryUrl],
                new WebLaunchTarget(
                    new Uri(entryUrl),
                    label,
                    true,
                    true)));

    private static void AssertRecoveryJournal(
        string dataDirectory,
        string expectedStatus,
        AgentState expectedState)
    {
        var journal = new SessionJournal(
            Path.Combine(dataDirectory, "session-journal.json")).Current;

        Assert.NotNull(journal);
        Assert.Equal(expectedState, journal.State);
        var recovery = journal.Steps.Last(
            step => step.Name == "AssignedAccessRecovery");
        Assert.Equal(expectedStatus, recovery.Status);
    }

    private sealed class ScriptSequence(
        params (string Name, string? Output, Exception? Exception)[] steps)
    {
        private readonly Queue<(string Name, string? Output, Exception? Exception)> remaining =
            new(steps);

        internal List<string> Calls { get; } = [];
        internal List<ScriptInvocation> Invocations { get; } = [];

        internal Task<string> RunAsync(
            string scriptName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(scriptName);
            Invocations.Add(new ScriptInvocation(scriptName, arguments));

            var step = remaining.Dequeue();
            Assert.Equal(step.Name, scriptName);
            if (step.Exception is not null)
            {
                return Task.FromException<string>(step.Exception);
            }

            return Task.FromResult(step.Output ?? string.Empty);
        }
    }

    private sealed record ScriptInvocation(
        string Name,
        IReadOnlyList<string> Arguments);

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "ExamKioskTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, true);
        }
    }
}