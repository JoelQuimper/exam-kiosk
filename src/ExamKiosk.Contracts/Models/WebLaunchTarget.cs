namespace ExamKiosk.Contracts;

public sealed record WebLaunchTarget(
    Uri EntryUrl,
    string Label,
    bool PinToStart,
    bool PinToTaskbar);
