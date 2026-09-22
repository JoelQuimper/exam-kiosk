using ExamKiosk.Contracts;

namespace ExamKiosk.Web.ExamSessions;

public interface IExamSessionStore
{
    ExamSession Start(
        string userPrincipalName,
        EffectiveExamProfile profile);

    ExamSession? Get(Guid sessionId);

    ExamSessionActivationStatus ActivateForDevice(
        Guid sessionId,
        string profileSha256);

    ExamSessionCompletionStatus CompleteForDevice(
        Guid sessionId,
        string profileSha256);
}

public enum ExamSessionActivationStatus
{
    Activated,
    NotFound,
    Conflict,
}

public enum ExamSessionCompletionStatus
{
    Completed,
    NotFound,
    Conflict,
}
