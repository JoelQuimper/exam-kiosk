namespace ExamKiosk.Web.ExamAssignments;

public sealed record StudentProfile(
    string StudentId,
    string UserPrincipalName,
    string DisplayName);

public sealed record ExamDefinition(
    string ExamId,
    string Title,
    string Icon);

public sealed record ToolDefinition(
    string ToolId,
    string DisplayName,
    string Icon);

public sealed record StudentExamAssignment(
    string AssignmentId,
    string StudentId,
    string ExamId,
    Uri SharePointFolderUrl,
    IReadOnlyList<string> ToolIds);

public sealed record AssignedExam(
    string AssignmentId,
    ExamDefinition Exam,
    IReadOnlyList<ToolDefinition> Tools);
