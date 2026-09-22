namespace ExamKiosk.DeviceAgent.WindowsConfiguration.Models;

public sealed record EffectiveWindowsConfiguration(
    AssignedAccessArtifact AssignedAccess,
    IReadOnlyList<WindowsShortcutArtifact> Shortcuts);
