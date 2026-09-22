namespace ExamKiosk.Contracts;

public sealed record DesktopToolDefinition(
    string ToolId,
    string DisplayName,
    string Icon,
    DesktopToolConfiguration Configuration)
    : ToolDefinition(ToolId, DisplayName, Icon);
