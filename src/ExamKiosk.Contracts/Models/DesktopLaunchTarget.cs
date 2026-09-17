namespace ExamKiosk.Contracts;

public sealed record DesktopLaunchTarget(
    string ApplicationId,
    string Label,
    bool PinToStart,
    bool PinToTaskbar);
