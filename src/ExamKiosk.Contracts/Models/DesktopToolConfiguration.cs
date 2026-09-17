namespace ExamKiosk.Contracts;

public sealed record DesktopToolConfiguration(
    IReadOnlyList<ApplicationDefinition> Applications,
    DesktopLaunchTarget LaunchTarget);
