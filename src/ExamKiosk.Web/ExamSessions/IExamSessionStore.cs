using ExamKiosk.Contracts;

namespace ExamKiosk.Web.ExamSessions;

public interface IExamSessionStore
{
    ExamSession Start(
        string userPrincipalName,
        EffectiveExamProfile profile);

    ExamSession? Get(Guid sessionId);

    ExamSessionActivationResult ActivateForDevice(
        Guid sessionId,
        string profileSha256);

    ExamSessionCompletionResult CompleteForDevice(
        Guid sessionId,
        string profileSha256);
}

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
