namespace ExamKiosk.Contracts;

public sealed record WebWindowsShortcutArtifact(
    string ShortcutId,
    string LinkPath,
    string Label,
    Uri EntryUrl)
    : WindowsShortcutArtifact(ShortcutId, LinkPath, Label);
