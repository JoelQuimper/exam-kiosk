using ExamKiosk.Contracts;

namespace ExamKiosk.Web.Controllers.Models;

public sealed record ActiveExamSessionResponse(
    Guid SessionId,
    ExamSessionState State,
    string ExamId,
    string ExamTitle)
{
    public static ActiveExamSessionResponse FromSession(ExamSession session) =>
        new(
            session.SessionId,
            session.State,
            session.Profile.Exam.Id,
            session.Profile.Exam.Title);
}
