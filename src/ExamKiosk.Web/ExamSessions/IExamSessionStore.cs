using ExamKiosk.Contracts;

namespace ExamKiosk.Web.ExamSessions;

public interface IExamSessionStore
{
    ExamSessionStartResult Start(
        string userPrincipalName,
        EffectiveExamProfile profile);

    ExamSession? Get(Guid sessionId);
}

public sealed record ExamSessionStartResult(
    bool Created,
    ExamSession Session);
