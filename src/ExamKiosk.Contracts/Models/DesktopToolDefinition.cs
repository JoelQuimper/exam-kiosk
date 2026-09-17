namespace ExamKiosk.Contracts;

public sealed record DesktopToolDefinition(
    string ToolId,
    string DisplayName,
    string Icon,
    bool Enabled,
    DesktopToolConfiguration Configuration)
    : ToolDefinition(ToolId, DisplayName, Icon, Enabled);
