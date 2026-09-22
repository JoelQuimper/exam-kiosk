using ExamKiosk.Contracts;

namespace ExamKiosk.DeviceAgent.Tests;

public sealed class SessionJournalTests
{
    [Fact]
    public async Task Journal_PersistsStateAndOrderedSteps()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var journalPath = Path.Combine(temporaryDirectory.Path, "session-journal.json");
        var journal = new SessionJournal(journalPath);

        await journal.BeginAsync(CancellationToken.None);
        var sessionId = journal.Current!.SessionId;
        await journal.RecordStepAsync("AssignedAccessApply", "started", null, CancellationToken.None);
        await journal.SetStateAsync(AgentState.InExam, CancellationToken.None);
        await journal.RecordStepAsync("OnExamStart", "completed", null, CancellationToken.None);

        var reloaded = new SessionJournal(journalPath).Current;

        Assert.NotNull(reloaded);
        Assert.Equal(sessionId, reloaded!.SessionId);
        Assert.Equal(AgentState.InExam, reloaded.State);
        Assert.Equal(
            ["AssignedAccessApply", "OnExamStart"],
            reloaded.Steps.Select(step => step.Name).ToArray());
        Assert.Equal("started", reloaded.Steps[0].Status);
        Assert.Equal("completed", reloaded.Steps[1].Status);
        Assert.False(File.Exists(journalPath + ".tmp"));
    }

    [Fact]
    public async Task Journal_PersistsStepErrors()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var journalPath = Path.Combine(temporaryDirectory.Path, "session-journal.json");
        var journal = new SessionJournal(journalPath);

        await journal.BeginAsync(CancellationToken.None);
        await journal.RecordStepAsync(
            "OnExamStart",
            "failed",
            "test failure",
            CancellationToken.None);

        var step = new SessionJournal(journalPath).Current!.Steps.Single(step => step.Name == "OnExamStart");

        Assert.Equal("failed", step.Status);
        Assert.Equal("test failure", step.Error);
    }

    [Fact]
    public async Task Journal_BeginWithProfileReceipt_PersistsBackendSession()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var journalPath = Path.Combine(temporaryDirectory.Path, "session-journal.json");
        var journal = new SessionJournal(journalPath);
        var sessionId = Guid.NewGuid();

        await journal.BeginAsync(
            sessionId,
            "abc123",
            "Exam title",
            new Uri("https://example.com/exam"),
            CancellationToken.None);

        var reloaded = new SessionJournal(journalPath).Current;
        Assert.NotNull(reloaded);
        Assert.Equal(sessionId, reloaded.SessionId);
        Assert.Equal("abc123", reloaded.ProfileSha256);
        Assert.Equal("Exam title", reloaded.ExamTitle);
        Assert.Equal(
            new Uri("https://example.com/exam"),
            reloaded.ExamEntryUrl);
    }

}
