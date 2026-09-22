using ExamKiosk.DeviceAgent.WindowsConfiguration.Models;

namespace ExamKiosk.DeviceAgent.WindowsConfiguration.AssignedAccess.Models;

internal sealed record AssignedAccessConfiguration(
    AssignedAccessArtifact AssignedAccess,
    IReadOnlyList<WindowsShortcutArtifact> Shortcuts);
