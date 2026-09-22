namespace ExamKiosk.Contracts;

public sealed record ExternalProtocolLaunchRule(
    string Protocol,
    IReadOnlyList<string> AllowedOrigins);
