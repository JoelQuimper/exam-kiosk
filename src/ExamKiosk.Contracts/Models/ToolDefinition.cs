using System.Text.Json.Serialization;

namespace ExamKiosk.Contracts;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(DesktopToolDefinition), "desktop")]
[JsonDerivedType(typeof(WebToolDefinition), "web")]
public abstract record ToolDefinition(
    string ToolId,
    string DisplayName,
    string Icon,
    bool Enabled);
