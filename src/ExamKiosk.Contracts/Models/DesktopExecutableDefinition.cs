namespace ExamKiosk.Contracts;

public sealed record DesktopExecutableDefinition(
    string ApplicationId,
    ApplicationRole Role,
    string Path,
    ExecutableValidation Validation)
    : ApplicationDefinition(ApplicationId, Role);
