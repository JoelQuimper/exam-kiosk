using ExamKiosk.Contracts;

namespace ExamKiosk.Web.ExamSessions;

public sealed class InMemoryExamSessionStore(TimeProvider timeProvider)
    : IExamSessionStore
{
    public static readonly TimeSpan StartingLifetime = TimeSpan.FromMinutes(15);

    private readonly Lock syncRoot = new();
    private readonly Dictionary<Guid, ExamSession> sessions = [];
    private readonly Dictionary<string, Guid> nonterminalSessionIdsByStudent =
        new(StringComparer.OrdinalIgnoreCase);

    public ExamSessionStartResult Start(
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
            var now = timeProvider.GetUtcNow();
            ExpireStartingSession(normalizedUpn, now);

            if (nonterminalSessionIdsByStudent.TryGetValue(
                    normalizedUpn,
                    out var existingSessionId))
            {
                return new ExamSessionStartResult(
                    false,
                    sessions[existingSessionId]);
            }

            var session = new ExamSession(
                Guid.NewGuid(),
                ExamSessionState.Starting,
                now,
                now.Add(StartingLifetime),
                profile);
            sessions.Add(session.SessionId, session);
            nonterminalSessionIdsByStudent.Add(normalizedUpn, session.SessionId);

            return new ExamSessionStartResult(true, session);
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
            if (!sessions.TryGetValue(sessionId, out var session))
            {
                return null;
            }

            if (session.State == ExamSessionState.Starting
                && session.ExpiresAtUtc <= timeProvider.GetUtcNow())
            {
                session = Expire(session);
            }

            return session;
        }
    }

    public ExamSessionCancellationResult Cancel(
        string userPrincipalName,
        Guid sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrincipalName);
        if (sessionId == Guid.Empty)
        {
            return new ExamSessionCancellationResult(
                ExamSessionCancellationStatus.NotFound,
                null);
        }

        var normalizedUpn = userPrincipalName.Trim();
        lock (syncRoot)
        {
            if (!sessions.TryGetValue(sessionId, out var session)
                || !string.Equals(
                    session.Profile.Student.UserPrincipalName,
                    normalizedUpn,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new ExamSessionCancellationResult(
                    ExamSessionCancellationStatus.NotFound,
                    null);
            }

            if (session.State == ExamSessionState.Starting
                && session.ExpiresAtUtc <= timeProvider.GetUtcNow())
            {
                session = Expire(session);
            }

            if (session.State == ExamSessionState.Cancelled)
            {
                return new ExamSessionCancellationResult(
                    ExamSessionCancellationStatus.Cancelled,
                    session);
            }

            if (session.State != ExamSessionState.Starting)
            {
                return new ExamSessionCancellationResult(
                    ExamSessionCancellationStatus.Conflict,
                    session);
            }

            var cancelled = session with
            {
                State = ExamSessionState.Cancelled,
                ExpiresAtUtc = null,
            };
            sessions[session.SessionId] = cancelled;
            nonterminalSessionIdsByStudent.Remove(normalizedUpn);
            return new ExamSessionCancellationResult(
                ExamSessionCancellationStatus.Cancelled,
                cancelled);
        }
    }

    private void ExpireStartingSession(
        string userPrincipalName,
        DateTimeOffset now)
    {
        if (!nonterminalSessionIdsByStudent.TryGetValue(
                userPrincipalName,
                out var sessionId))
        {
            return;
        }

        var session = sessions[sessionId];
        if (session.State == ExamSessionState.Starting
            && session.ExpiresAtUtc <= now)
        {
            Expire(session);
        }
    }

    private ExamSession Expire(ExamSession session)
    {
        var expired = session with
        {
            State = ExamSessionState.Expired,
            ExpiresAtUtc = null,
        };
        sessions[session.SessionId] = expired;
        nonterminalSessionIdsByStudent.Remove(
            session.Profile.Student.UserPrincipalName);
        return expired;
    }
}
