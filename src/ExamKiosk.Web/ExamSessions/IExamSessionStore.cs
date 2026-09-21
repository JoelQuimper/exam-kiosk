using ExamKiosk.Contracts;

namespace ExamKiosk.Web.ExamSessions;

public interface IExamSessionStore
{
    ExamSessionStartResult Start(
        string userPrincipalName,
        EffectiveExamProfile profile);

    ExamSession? Get(Guid sessionId);

    ExamSessionCancellationResult Cancel(
        string userPrincipalName,
        Guid sessionId);

    ExamSessionCompletionResult CompleteActive(
        string userPrincipalName);
}

public sealed record ExamSessionStartResult(
    bool Created,
    ExamSession Session);

public enum ExamSessionCancellationStatus
{
    Cancelled,
    NotFound,
    Conflict,
}

public sealed record ExamSessionCancellationResult(
    ExamSessionCancellationStatus Status,
    ExamSession? Session);

public enum ExamSessionCompletionStatus
{
    Completed,
    NotFound,
}

public sealed record ExamSessionCompletionResult(
    ExamSessionCompletionStatus Status,
    ExamSession? Session);
