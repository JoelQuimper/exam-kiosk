using ExamKiosk.Contracts;

namespace ExamKiosk.DeviceAgent.Tests;

public sealed class TransitionManagerTests
{
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
}