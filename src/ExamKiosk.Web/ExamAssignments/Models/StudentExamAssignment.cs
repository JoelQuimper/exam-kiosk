namespace ExamKiosk.Web.ExamAssignments.Models;

internal sealed record StudentExamAssignment(
    string AssignmentId,
    string StudentId,
    string ExamId,
    Uri SharePointFolderUrl,
    IReadOnlyList<string> ToolIds);
