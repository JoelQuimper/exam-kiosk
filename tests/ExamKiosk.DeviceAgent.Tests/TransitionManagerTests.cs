using ExamKiosk.Contracts;

namespace ExamKiosk.DeviceAgent.Tests;

public sealed class TransitionManagerTests
{
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