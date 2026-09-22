namespace ExamKiosk.Contracts;

public sealed record WebToolDefinition(
    string ToolId,
    string DisplayName,
    string Icon,
    WebToolConfiguration Configuration)
    : ToolDefinition(ToolId, DisplayName, Icon);
