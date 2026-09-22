namespace ExamKiosk.DeviceAgent.WindowsConfiguration.Models;

public sealed record DesktopWindowsShortcutArtifact(
    string ShortcutId,
    string LinkPath,
    string Label)
    : WindowsShortcutArtifact(ShortcutId, LinkPath, Label);
