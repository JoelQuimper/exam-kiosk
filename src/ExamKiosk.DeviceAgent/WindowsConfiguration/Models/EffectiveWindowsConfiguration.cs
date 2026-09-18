namespace ExamKiosk.DeviceAgent.WindowsConfiguration.Models;

public sealed record EffectiveWindowsConfiguration(
    WindowsClientVersion ClientVersion,
    AssignedAccessArtifact AssignedAccess,
    IReadOnlyList<WindowsShortcutArtifact> Shortcuts);
