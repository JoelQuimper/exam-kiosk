namespace ExamKiosk.Contracts;

public sealed record PackagedApplicationDefinition(
    string ApplicationId,
    ApplicationRole Role,
    string AppUserModelId)
    : ApplicationDefinition(ApplicationId, Role);
