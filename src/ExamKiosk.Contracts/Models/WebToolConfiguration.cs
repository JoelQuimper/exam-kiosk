namespace ExamKiosk.Contracts;

public sealed record WebToolConfiguration(
    IReadOnlyList<string> AllowedUrls,
    WebLaunchTarget LaunchTarget,
    string? ShortcutIconLocation = null);
