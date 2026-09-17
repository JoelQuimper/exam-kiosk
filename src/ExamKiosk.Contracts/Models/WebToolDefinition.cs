namespace ExamKiosk.Contracts;

public sealed record WebToolDefinition(
    string ToolId,
    string DisplayName,
    string Icon,
    bool Enabled,
    WebToolConfiguration Configuration)
    : ToolDefinition(ToolId, DisplayName, Icon, Enabled);
