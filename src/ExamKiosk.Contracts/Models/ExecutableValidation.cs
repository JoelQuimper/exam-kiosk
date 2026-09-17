namespace ExamKiosk.Contracts;

public sealed record ExecutableValidation(
    string? PublisherSubject,
    string? Sha256);
