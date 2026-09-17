namespace ExamKiosk.Contracts;

public sealed record WebToolConfiguration(
    IReadOnlyList<string> EdgeAllowlist,
    WebLaunchTarget LaunchTarget);
