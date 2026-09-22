namespace ExamKiosk.Contracts;

public sealed record DesktopExecutableDefinition(
    string ApplicationId,
    string Path,
    string? DesktopApplicationId,
    ExecutableValidation Validation)
    : ApplicationDefinition(ApplicationId);
