using ExamKiosk.Contracts;

namespace ExamKiosk.Web.ExamAssignments.Models;

public sealed record AssignedExam(
    string AssignmentId,
    ExamDefinition Exam,
    Uri SharePointFolderUrl,
    IReadOnlyList<ToolDefinition> Tools);
