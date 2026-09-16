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
        var startInfo = MainWindow.CreateEdgeStartInfo();

        Assert.EndsWith(
            @"Microsoft\Edge\Application\msedge.exe",
            startInfo.FileName,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            ["--new-window", "--no-first-run", "--inprivate", "https://www.example.com/"],
            startInfo.ArgumentList);
        Assert.False(startInfo.UseShellExecute);
    }
}
