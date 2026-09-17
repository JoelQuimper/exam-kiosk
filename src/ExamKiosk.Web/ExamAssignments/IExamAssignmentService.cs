using ExamKiosk.Web.ExamAssignments.Models;

namespace ExamKiosk.Web.ExamAssignments;

public interface IExamAssignmentService
{
    IReadOnlyList<AssignedExam> GetAssignments(string userPrincipalName);

    AssignedExam? GetAssignment(
        string userPrincipalName,
        string assignmentId);
}
