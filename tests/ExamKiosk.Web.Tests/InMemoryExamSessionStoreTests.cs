using ExamKiosk.Contracts;
using ExamKiosk.Web.ExamSessions;

namespace ExamKiosk.Web.Tests;

public sealed class InMemoryExamSessionStoreTests
{
    [Fact]
    public void Start_CreatesStartingSessionWithBoundedExpiry()
    {
        var now = new DateTimeOffset(2026, 9, 18, 13, 0, 0, TimeSpan.Zero);
        var timeProvider = new TestTimeProvider(now);
        var store = new InMemoryExamSessionStore(timeProvider);

        var result = store.Start("student@example.com", CreateProfile());

        Assert.True(result.Created);
        Assert.NotEqual(Guid.Empty, result.Session.SessionId);
        Assert.Equal(ExamSessionState.Starting, result.Session.State);
        Assert.Equal(now, result.Session.CreatedAtUtc);
        Assert.Equal(
            now.Add(InMemoryExamSessionStore.StartingLifetime),
            result.Session.ExpiresAtUtc);
    }

    [Fact]
    public void Start_WhenStudentHasNonterminalSession_ReturnsConflict()
    {
        var store = new InMemoryExamSessionStore(
            new TestTimeProvider(DateTimeOffset.UtcNow));
        var first = store.Start("student@example.com", CreateProfile());

        var second = store.Start("STUDENT@example.com", CreateProfile());

        Assert.False(second.Created);
        Assert.Equal(first.Session, second.Session);
    }

    [Fact]
    public void Start_AfterStartingSessionExpires_CreatesNewSession()
    {
        var timeProvider = new TestTimeProvider(DateTimeOffset.UtcNow);
        var store = new InMemoryExamSessionStore(timeProvider);
        var first = store.Start("student@example.com", CreateProfile());
        timeProvider.Advance(InMemoryExamSessionStore.StartingLifetime);

        var second = store.Start("student@example.com", CreateProfile());

        Assert.True(second.Created);
        Assert.NotEqual(first.Session.SessionId, second.Session.SessionId);
        Assert.Equal(
            ExamSessionState.Expired,
            store.Get(first.Session.SessionId)?.State);
    }

    [Fact]
    public async Task Start_WhenRequestsAreConcurrent_CreatesExactlyOneSession()
    {
        var store = new InMemoryExamSessionStore(
            new TestTimeProvider(DateTimeOffset.UtcNow));
        var starts = Enumerable.Range(0, 16)
            .Select(
                _ => Task.Run(
                    () => store.Start(
                        "student@example.com",
                        CreateProfile())))
            .ToArray();

        var results = await Task.WhenAll(starts);

        Assert.Single(results, result => result.Created);
        Assert.Single(results.Select(result => result.Session.SessionId).Distinct());
    }

    [Fact]
    public void Cancel_WhenSessionIsStarting_CancelsAndAllowsAnotherStart()
    {
        var store = new InMemoryExamSessionStore(
            new TestTimeProvider(DateTimeOffset.UtcNow));
        var first = store.Start("student@example.com", CreateProfile());

        var cancellation = store.Cancel(
            "student@example.com",
            first.Session.SessionId);
        var second = store.Start("student@example.com", CreateProfile());

        Assert.Equal(
            ExamSessionCancellationStatus.Cancelled,
            cancellation.Status);
        Assert.Equal(ExamSessionState.Cancelled, cancellation.Session?.State);
        Assert.Null(cancellation.Session?.ExpiresAtUtc);
        Assert.True(second.Created);
    }

    [Fact]
    public void CompleteActive_CompletesSessionAndAllowsAnotherStart()
    {
        var store = new InMemoryExamSessionStore(
            new TestTimeProvider(DateTimeOffset.UtcNow));
        var first = store.Start("student@example.com", CreateProfile());

        var completion = store.CompleteActive("STUDENT@example.com");
        var second = store.Start("student@example.com", CreateProfile());

        Assert.Equal(
            ExamSessionCompletionStatus.Completed,
            completion.Status);
        Assert.Equal(first.Session.SessionId, completion.Session?.SessionId);
        Assert.Equal(ExamSessionState.Completed, completion.Session?.State);
        Assert.Null(completion.Session?.ExpiresAtUtc);
        Assert.True(second.Created);
    }

    [Fact]
    public void CompleteActive_WhenStudentHasNoSession_ReturnsNotFound()
    {
        var store = new InMemoryExamSessionStore(
            new TestTimeProvider(DateTimeOffset.UtcNow));

        var completion = store.CompleteActive("student@example.com");

        Assert.Equal(
            ExamSessionCompletionStatus.NotFound,
            completion.Status);
        Assert.Null(completion.Session);
    }

    [Fact]
    public void Cancel_WhenAlreadyCancelled_IsIdempotent()
    {
        var store = new InMemoryExamSessionStore(
            new TestTimeProvider(DateTimeOffset.UtcNow));
        var started = store.Start("student@example.com", CreateProfile());
        var first = store.Cancel(
            "student@example.com",
            started.Session.SessionId);

        var second = store.Cancel(
            "STUDENT@example.com",
            started.Session.SessionId);

        Assert.Equal(
            ExamSessionCancellationStatus.Cancelled,
            second.Status);
        Assert.Equal(first.Session, second.Session);
    }

    [Fact]
    public void Cancel_WhenSessionBelongsToAnotherStudent_ReturnsNotFound()
    {
        var store = new InMemoryExamSessionStore(
            new TestTimeProvider(DateTimeOffset.UtcNow));
        var started = store.Start("student@example.com", CreateProfile());

        var result = store.Cancel(
            "other@example.com",
            started.Session.SessionId);

        Assert.Equal(ExamSessionCancellationStatus.NotFound, result.Status);
        Assert.Null(result.Session);
        Assert.Equal(
            ExamSessionState.Starting,
            store.Get(started.Session.SessionId)?.State);
    }

    [Fact]
    public void Cancel_WhenStartingSessionExpired_ReturnsConflict()
    {
        var timeProvider = new TestTimeProvider(DateTimeOffset.UtcNow);
        var store = new InMemoryExamSessionStore(timeProvider);
        var started = store.Start("student@example.com", CreateProfile());
        timeProvider.Advance(InMemoryExamSessionStore.StartingLifetime);

        var result = store.Cancel(
            "student@example.com",
            started.Session.SessionId);

        Assert.Equal(ExamSessionCancellationStatus.Conflict, result.Status);
        Assert.Equal(ExamSessionState.Expired, result.Session?.State);
    }

    [Fact]
    public void Start_WhenProfileBelongsToAnotherStudent_Throws()
    {
        var store = new InMemoryExamSessionStore(
            new TestTimeProvider(DateTimeOffset.UtcNow));

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
                new Uri("https://example.com/exam"),
                new WebLaunchTarget(
                    new Uri("https://example.com/exam"),
                    "Open exam",
                    true,
                    true)),
            [],
            new EffectiveEdgePolicy(["*"], ["https://example.com"]));

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan duration)
        {
            utcNow = utcNow.Add(duration);
        }
    }
}
