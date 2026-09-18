namespace ExamKiosk.Contracts;

public sealed record DesktopExecutableDefinition(
    string ApplicationId,
    ApplicationRole Role,
    string Path,
    string? DesktopApplicationId,
    ExecutableValidation Validation)
    : ApplicationDefinition(ApplicationId, Role);
