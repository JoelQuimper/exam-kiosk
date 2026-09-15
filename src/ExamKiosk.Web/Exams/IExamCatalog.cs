namespace ExamKiosk.Web.Exams;

public interface IExamCatalog
{
    IReadOnlyList<ExamSummary> GetAssignedExams();
}
