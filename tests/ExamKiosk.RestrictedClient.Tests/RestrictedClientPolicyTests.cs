using ExamKiosk.Contracts;

namespace ExamKiosk.RestrictedClient.Tests;

public sealed class RestrictedClientPolicyTests
{
    [Fact]
    public void NavigationPolicy_AllowsWebAppAndEntraOrigins()
    {
        var configuration = WebViewHostConfiguration.Parse(
            """{"webAppUrl":"https://exam.example.test"}""",
            "/exam-session");
        var policy = new WebViewNavigationPolicy(
            configuration,
            [new Uri("https://login.microsoftonline.com")]);

        Assert.True(policy.IsAllowed("https://exam.example.test/exam-session"));
        Assert.True(policy.IsAllowed("https://login.microsoftonline.com/tenant/oauth2/v2.0/authorize"));
        Assert.False(policy.IsAllowed("https://evil.example.test/"));
    }

    [Fact]
    public void CreateEdgeStartInfo_UsesOnlyFixedExamUrlAndSwitches()
    {
        var startInfo = MainWindow.CreateEdgeStartInfo(
            new Uri("https://sharepoint.example.test/exams/student-1"));

        Assert.EndsWith(
            @"Microsoft\Edge\Application\msedge.exe",
            startInfo.FileName,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            [
                "--new-window",
                "--no-first-run",
                "--inprivate",
                "https://sharepoint.example.test/exams/student-1",
            ],
            startInfo.ArgumentList);
        Assert.False(startInfo.UseShellExecute);
    }

    [Theory]
    [InlineData("http://sharepoint.example.test/exam")]
    [InlineData("https://user@sharepoint.example.test/exam")]
    [InlineData("/relative")]
    public void CreateEdgeStartInfo_WithInvalidDestination_Throws(string destination)
    {
        Assert.Throws<ArgumentException>(
            () => MainWindow.CreateEdgeStartInfo(
                new Uri(destination, UriKind.RelativeOrAbsolute)));
    }
}
