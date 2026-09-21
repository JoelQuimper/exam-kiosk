namespace ExamKiosk.DeviceAgent.WindowsConfiguration.Models;

public sealed record WebWindowsShortcutArtifact(
    string ShortcutId,
    string LinkPath,
    string Label,
    Uri EntryUrl,
    string? IconLocation)
    : WindowsShortcutArtifact(ShortcutId, LinkPath, Label);
