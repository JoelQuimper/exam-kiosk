namespace ExamKiosk.DeviceAgent.WindowsConfiguration.Models;

public sealed record WebWindowsShortcutArtifact(
    string ShortcutId,
    string LinkPath,
    string Label,
    Uri EntryUrl)
    : WindowsShortcutArtifact(ShortcutId, LinkPath, Label);
