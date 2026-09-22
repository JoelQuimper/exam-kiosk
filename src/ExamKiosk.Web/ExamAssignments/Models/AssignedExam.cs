using ExamKiosk.Contracts;

namespace ExamKiosk.Web.ExamAssignments.Models;

public sealed record AssignedExam(
    string AssignmentId,
    ExamDefinition Exam,
    Uri ExamTarget,
    IReadOnlyList<string> AllowedUrls,
    IReadOnlyList<ToolDefinition> Tools);
