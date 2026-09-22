namespace ExamKiosk.Contracts;

public sealed record EffectiveExamProfile(
    int SchemaVersion,
    string AssignmentId,
    EffectiveStudent Student,
    EffectiveExam Exam,
    IReadOnlyList<ToolDefinition> Tools,
    IReadOnlyList<string> AllowedUrls,
    IReadOnlyList<ExternalProtocolLaunchRule> ExternalProtocolLaunchRules);
