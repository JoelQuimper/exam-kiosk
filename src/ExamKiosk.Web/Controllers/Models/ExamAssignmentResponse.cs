using ExamKiosk.Web.ExamAssignments.Models;

namespace ExamKiosk.Web.Controllers.Models;

public sealed record ExamAssignmentResponse(
    string AssignmentId,
    ExamResponse Exam,
    IReadOnlyList<ToolResponse> Tools)
{
    internal static ExamAssignmentResponse FromAssignedExam(AssignedExam assignment) =>
        new(
            assignment.AssignmentId,
            new ExamResponse(
                assignment.Exam.ExamId,
                assignment.Exam.Title,
                assignment.Exam.Icon),
            assignment.Tools
                .Select(tool => new ToolResponse(
                    tool.ToolId,
                    tool.DisplayName,
                    tool.Icon))
                .ToArray());
}
