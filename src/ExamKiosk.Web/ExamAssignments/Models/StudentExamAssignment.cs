namespace ExamKiosk.Web.ExamAssignments.Models;

internal sealed record StudentExamAssignment(
    string AssignmentId,
    string StudentId,
    string ExamId,
    Uri ExamTarget,
    IReadOnlyList<string> AllowedUrls,
    IReadOnlyList<string> ToolIds);
