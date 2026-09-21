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

    ExamSessionActivationResult ActivateForDevice(
        Guid sessionId,
        string profileSha256);

    ExamSessionCompletionResult CompleteForDevice(
        Guid sessionId,
        string profileSha256);
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

public enum ExamSessionActivationStatus
{
    Activated,
    NotFound,
    Conflict,
}

public sealed record ExamSessionActivationResult(
    ExamSessionActivationStatus Status,
    ExamSession? Session);

public enum ExamSessionCompletionStatus
{
    Completed,
    NotFound,
    Conflict,
}

public sealed record ExamSessionCompletionResult(
    ExamSessionCompletionStatus Status,
    ExamSession? Session);
