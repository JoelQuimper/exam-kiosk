namespace ExamKiosk.WindowsConfiguration.Models;

public sealed record DesktopWindowsShortcutArtifact(
    string ShortcutId,
    string LinkPath,
    string Label,
    string ApplicationId)
    : WindowsShortcutArtifact(ShortcutId, LinkPath, Label);
