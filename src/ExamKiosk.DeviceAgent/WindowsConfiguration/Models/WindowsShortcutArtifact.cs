using System.Text.Json.Serialization;

namespace ExamKiosk.DeviceAgent.WindowsConfiguration.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(DesktopWindowsShortcutArtifact), "desktop")]
[JsonDerivedType(typeof(WebWindowsShortcutArtifact), "web")]
public abstract record WindowsShortcutArtifact(
    string ShortcutId,
    string LinkPath,
    string Label);
