namespace ExamKiosk.Contracts;

public sealed record DesktopExecutableDefinition(
    string ApplicationId,
    string Path,
    string? DesktopApplicationId)
    : ApplicationDefinition(ApplicationId);
