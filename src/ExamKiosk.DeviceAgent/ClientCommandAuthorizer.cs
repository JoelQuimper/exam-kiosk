using ExamKiosk.Contracts;

namespace ExamKiosk.DeviceAgent;

internal static class ClientCommandAuthorizer
{
    internal static bool IsAuthorized(
        string executablePath,
        int sessionId,
        string installRoot,
        AgentCommand command)
    {
        if (sessionId == 0 ||
            string.IsNullOrWhiteSpace(executablePath) ||
            string.IsNullOrWhiteSpace(installRoot))
        {
            return false;
        }

        var launcherPath = Path.Combine(
            installRoot,
            "Launcher",
            "ExamKiosk.Launcher.exe");
        var restrictedClientPath = Path.Combine(
            installRoot,
            "RestrictedClient",
            "ExamKiosk.RestrictedClient.exe");

        return command switch
        {
            AgentCommand.GetStatus =>
                PathsEqual(executablePath, launcherPath) ||
                PathsEqual(executablePath, restrictedClientPath),
            AgentCommand.GetActiveExam =>
                PathsEqual(executablePath, restrictedClientPath),
            AgentCommand.StartExam => PathsEqual(executablePath, launcherPath),
            AgentCommand.FinishExam => PathsEqual(executablePath, restrictedClientPath),
            _ => false
        };
    }

    private static bool PathsEqual(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
        {
            return false;
        }

        return string.Equals(
            Path.GetFullPath(first),
            Path.GetFullPath(second),
            StringComparison.OrdinalIgnoreCase);
    }
}