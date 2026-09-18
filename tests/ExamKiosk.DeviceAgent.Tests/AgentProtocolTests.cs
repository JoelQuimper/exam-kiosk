using ExamKiosk.Contracts;

namespace ExamKiosk.DeviceAgent.Tests;

public sealed class AgentProtocolTests
{
    [Fact]
    public void TryValidateRequest_WithValidStartPayload_ReturnsTrue()
    {
        var request = StartRequest();

        var valid = AgentProtocol.TryValidateRequest(request, out var error);

        Assert.True(valid);
        Assert.Null(error);
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public void TryValidateRequest_WithInvalidShape_ReturnsFalse(
        AgentRequest request)
    {
        var valid = AgentProtocol.TryValidateRequest(request, out var error);

        Assert.False(valid);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public async Task SendAsync_WhenStartExamHasNoPayload_ThrowsBeforeConnecting()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => AgentClient.SendAsync(
                AgentCommand.StartExam,
                TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task StartExamAsync_WhenIntentExceedsLimit_ThrowsBeforeConnecting()
    {
        var profile = EffectiveProfileTestData.Create();
        profile = profile with
        {
            Exam = profile.Exam with
            {
                Title = new string('x', AgentProtocol.MaximumMessageLength),
            },
        };

        await Assert.ThrowsAsync<InvalidDataException>(
            () => AgentClient.StartExamAsync(
                new AgentStartExamPayload(Guid.NewGuid(), profile),
                TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void EffectiveProfileDigest_ForSameProfile_IsDeterministic()
    {
        var profile = EffectiveProfileTestData.Create();

        var first = EffectiveProfileDigest.Compute(profile);
        var second = EffectiveProfileDigest.Compute(profile);

        Assert.Equal(first, second);
        Assert.Matches("^[0-9a-f]{64}$", first);
    }

    public static TheoryData<AgentRequest> InvalidRequests =>
        new()
        {
            new(
                AgentProtocol.Version - 1,
                Guid.NewGuid(),
                AgentCommand.GetStatus),
            new(
                AgentProtocol.Version,
                Guid.Empty,
                AgentCommand.GetStatus),
            new(
                AgentProtocol.Version,
                Guid.NewGuid(),
                (AgentCommand)999),
            new(
                AgentProtocol.Version,
                Guid.NewGuid(),
                AgentCommand.GetStatus,
                new AgentStartExamPayload(
                    Guid.NewGuid(),
                    EffectiveProfileTestData.Create())),
            new(
                AgentProtocol.Version,
                Guid.NewGuid(),
                AgentCommand.StartExam),
            new(
                AgentProtocol.Version,
                Guid.NewGuid(),
                AgentCommand.StartExam,
                new AgentStartExamPayload(
                    Guid.Empty,
                    EffectiveProfileTestData.Create())),
        };

    private static AgentRequest StartRequest() =>
        new(
            AgentProtocol.Version,
            Guid.NewGuid(),
            AgentCommand.StartExam,
            new AgentStartExamPayload(
                Guid.NewGuid(),
                EffectiveProfileTestData.Create()));
}
