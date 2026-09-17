namespace ExamKiosk.Web.ExamAssignments;

public interface IExamAssignmentService
{
    IReadOnlyList<AssignedExam> GetAssignments(string userPrincipalName);
}
