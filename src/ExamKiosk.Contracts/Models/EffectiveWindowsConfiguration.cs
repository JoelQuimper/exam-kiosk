namespace ExamKiosk.Contracts;

public sealed record EffectiveWindowsConfiguration(
    WindowsClientVersion ClientVersion,
    AssignedAccessArtifact AssignedAccess,
    IReadOnlyList<WindowsShortcutArtifact> Shortcuts);
