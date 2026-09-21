using ExamKiosk.Contracts;
using ExamKiosk.Web.ExamSessions;

namespace ExamKiosk.Web.Tests;

public sealed class InMemoryExamSessionStoreTests
{
    [Fact]
    public void Start_CreatesStartingSession()
    {
        var store = new InMemoryExamSessionStore();

        var session = store.Start("student@example.com", CreateProfile());

        Assert.NotEqual(Guid.Empty, session.SessionId);
        Assert.Equal(ExamSessionState.Starting, session.State);
        Assert.Equal(session, store.Get(session.SessionId));
    }

    [Fact]
    public void Start_WhenStudentHasSession_ReplacesIt()
    {
        var store = new InMemoryExamSessionStore();
        var first = store.Start("student@example.com", CreateProfile());

        var second = store.Start("STUDENT@example.com", CreateProfile());

        Assert.NotEqual(first.SessionId, second.SessionId);
        Assert.Null(store.Get(first.SessionId));
        Assert.Equal(second, store.Get(second.SessionId));
    }

    [Fact]
    public void ActivateForDevice_ActivatesStartingSessionIdempotently()
    {
        var store = new InMemoryExamSessionStore();
        var profile = CreateProfile();
        var profileSha256 = EffectiveProfileDigest.Compute(profile);
        var started = store.Start("student@example.com", profile);

        var first = store.ActivateForDevice(
            started.SessionId,
            profileSha256);
        var second = store.ActivateForDevice(
            started.SessionId,
            profileSha256);

        Assert.Equal(ExamSessionActivationStatus.Activated, first.Status);
        Assert.Equal(ExamSessionState.Active, first.Session?.State);
        Assert.Equal(first.Session, second.Session);
    }

    [Fact]
    public void CompleteForDevice_CompletesActiveSessionIdempotently()
    {
        var store = new InMemoryExamSessionStore();
        var profile = CreateProfile();
        var profileSha256 = EffectiveProfileDigest.Compute(profile);
        var started = store.Start("student@example.com", profile);
        store.ActivateForDevice(started.SessionId, profileSha256);

        var first = store.CompleteForDevice(
            started.SessionId,
            profileSha256);
        var second = store.CompleteForDevice(
            started.SessionId,
            profileSha256);

        Assert.Equal(ExamSessionCompletionStatus.Completed, first.Status);
        Assert.Equal(ExamSessionState.Completed, first.Session?.State);
        Assert.Equal(first.Session, second.Session);
    }

    [Fact]
    public void CompleteForDevice_WhenSessionIsStarting_ReturnsConflict()
    {
        var store = new InMemoryExamSessionStore();
        var profile = CreateProfile();
        var started = store.Start("student@example.com", profile);

        var result = store.CompleteForDevice(
            started.SessionId,
            EffectiveProfileDigest.Compute(profile));

        Assert.Equal(ExamSessionCompletionStatus.Conflict, result.Status);
        Assert.Equal(started, result.Session);
    }

    [Fact]
    public void ActivateForDevice_WhenDigestDoesNotMatch_ReturnsNotFound()
    {
        var store = new InMemoryExamSessionStore();
        var started = store.Start("student@example.com", CreateProfile());

        var result = store.ActivateForDevice(
            started.SessionId,
            new string('0', 64));

        Assert.Equal(ExamSessionActivationStatus.NotFound, result.Status);
        Assert.Null(result.Session);
    }

    [Fact]
    public void Start_WhenProfileBelongsToAnotherStudent_Throws()
    {
        var store = new InMemoryExamSessionStore();

        Assert.Throws<ArgumentException>(
            () => store.Start("other@example.com", CreateProfile()));
    }

    private static EffectiveExamProfile CreateProfile() =>
        new(
            1,
            "assignment-1",
            new EffectiveStudent("student@example.com"),
            new EffectiveExam(
                "exam-1",
                "Exam",
                "exam",
                new Uri("https://example.com/exam")),
            [],
            ["https://example.com"]);
}
