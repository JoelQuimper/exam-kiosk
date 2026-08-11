using ExamKiosk.Contracts;

namespace ExamKiosk.DeviceAgent.Tests;

public sealed class ClientCommandAuthorizerTests
{
    private const string InstallRoot = @"C:\Program Files\ExamKiosk";
    private const string LauncherPath =
        @"C:\Program Files\ExamKiosk\Launcher\ExamKiosk.Launcher.exe";
    private const string RestrictedClientPath =
        @"C:\Program Files\ExamKiosk\RestrictedClient\ExamKiosk.RestrictedClient.exe";

    [Theory]
    [InlineData(LauncherPath, AgentCommand.StartExam)]
    [InlineData(LauncherPath, AgentCommand.GetStatus)]
    [InlineData(RestrictedClientPath, AgentCommand.FinishExam)]
    [InlineData(RestrictedClientPath, AgentCommand.GetStatus)]
    public void IsAuthorized_ForExpectedInteractiveClient_ReturnsTrue(
        string executablePath,
        AgentCommand command)
    {
        var result = ClientCommandAuthorizer.IsAuthorized(
            executablePath,
            1,
            InstallRoot,
            command);

        Assert.True(result);
    }

    [Theory]
    [InlineData(LauncherPath, AgentCommand.FinishExam)]
    [InlineData(RestrictedClientPath, AgentCommand.StartExam)]
    [InlineData(@"C:\Temp\ExamKiosk.Launcher.exe", AgentCommand.StartExam)]
    [InlineData(@"C:\Temp\ExamKiosk.RestrictedClient.exe", AgentCommand.FinishExam)]
    public void IsAuthorized_ForWrongClientOrPath_ReturnsFalse(
        string executablePath,
        AgentCommand command)
    {
        var result = ClientCommandAuthorizer.IsAuthorized(
            executablePath,
            1,
            InstallRoot,
            command);

        Assert.False(result);
    }

    [Theory]
    [InlineData(LauncherPath, AgentCommand.StartExam)]
    [InlineData(RestrictedClientPath, AgentCommand.FinishExam)]
    public void IsAuthorized_ForServiceSession_ReturnsFalse(
        string executablePath,
        AgentCommand command)
    {
        var result = ClientCommandAuthorizer.IsAuthorized(
            executablePath,
            0,
            InstallRoot,
            command);

        Assert.False(result);
    }
}