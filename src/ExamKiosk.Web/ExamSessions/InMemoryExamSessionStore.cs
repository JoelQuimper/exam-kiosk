using ExamKiosk.Contracts;

namespace ExamKiosk.Web.ExamSessions;

public sealed class InMemoryExamSessionStore : IExamSessionStore
{
    private readonly Lock syncRoot = new();
    private readonly Dictionary<Guid, ExamSession> sessions = [];
    private readonly Dictionary<string, Guid> sessionIdsByStudent =
        new(StringComparer.OrdinalIgnoreCase);

    public ExamSession Start(
        string userPrincipalName,
        EffectiveExamProfile profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrincipalName);
        ArgumentNullException.ThrowIfNull(profile);

        var normalizedUpn = userPrincipalName.Trim();
        if (!string.Equals(
                profile.Student.UserPrincipalName,
                normalizedUpn,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The effective profile does not belong to the supplied student.",
                nameof(profile));
        }

        lock (syncRoot)
        {
            if (sessionIdsByStudent.TryGetValue(
                    normalizedUpn,
                    out var previousSessionId))
            {
                sessions.Remove(previousSessionId);
            }

            var session = new ExamSession(
                Guid.NewGuid(),
                ExamSessionState.Starting,
                DateTimeOffset.UtcNow,
                profile);
            sessions.Add(session.SessionId, session);
            sessionIdsByStudent[normalizedUpn] = session.SessionId;

            return session;
        }
    }

    public ExamSession? Get(Guid sessionId)
    {
        if (sessionId == Guid.Empty)
        {
            return null;
        }

        lock (syncRoot)
        {
            return sessions.GetValueOrDefault(sessionId);
        }
    }

    public ExamSessionActivationStatus ActivateForDevice(
        Guid sessionId,
        string profileSha256)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileSha256);

        lock (syncRoot)
        {
            if (!TryGetMatchingSession(sessionId, profileSha256, out var session))
            {
                return ExamSessionActivationStatus.NotFound;
            }

            if (session.State == ExamSessionState.Active)
            {
                return ExamSessionActivationStatus.Activated;
            }

            if (session.State != ExamSessionState.Starting)
            {
                return ExamSessionActivationStatus.Conflict;
            }

            var active = session with { State = ExamSessionState.Active };
            sessions[sessionId] = active;
            return ExamSessionActivationStatus.Activated;
        }
    }

    public ExamSessionCompletionStatus CompleteForDevice(
        Guid sessionId,
        string profileSha256)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileSha256);

        lock (syncRoot)
        {
            if (!TryGetMatchingSession(sessionId, profileSha256, out var session))
            {
                return ExamSessionCompletionStatus.NotFound;
            }

            if (session.State == ExamSessionState.Completed)
            {
                return ExamSessionCompletionStatus.Completed;
            }

            if (session.State != ExamSessionState.Active)
            {
                return ExamSessionCompletionStatus.Conflict;
            }

            var completed = session with { State = ExamSessionState.Completed };
            sessions[sessionId] = completed;
            sessionIdsByStudent.Remove(
                session.Profile.Student.UserPrincipalName);
            return ExamSessionCompletionStatus.Completed;
        }
    }

    private bool TryGetMatchingSession(
        Guid sessionId,
        string profileSha256,
        out ExamSession session)
    {
        if (sessions.TryGetValue(sessionId, out session!)
            && string.Equals(
                EffectiveProfileDigest.Compute(session.Profile),
                profileSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        session = null!;
        return false;
    }

}
