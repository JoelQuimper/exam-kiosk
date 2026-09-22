namespace ExamKiosk.Contracts;

public sealed record PackagedApplicationDefinition(
    string ApplicationId,
    string AppUserModelId)
    : ApplicationDefinition(ApplicationId);
