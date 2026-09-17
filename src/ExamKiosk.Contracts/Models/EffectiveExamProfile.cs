namespace ExamKiosk.Contracts;

public sealed record EffectiveExamProfile(
    int SchemaVersion,
    string AssignmentId,
    EffectiveStudent Student,
    EffectiveExam Exam,
    IReadOnlyList<ToolDefinition> Tools,
    EffectiveEdgePolicy EdgePolicy,
    EffectiveWindowsConfiguration WindowsConfiguration);
