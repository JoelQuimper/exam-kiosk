using ExamKiosk.Contracts;
using ExamKiosk.Launcher;

namespace ExamKiosk.Launcher.Tests;

public sealed class LauncherNavigationPolicyTests
{
    private readonly WebViewNavigationPolicy policy = new(
        WebViewHostConfiguration.Parse(
            """{"webAppUrl":"https://exam.example.test"}""",
            "/exams"),
        [new Uri("https://login.microsoftonline.com")]);

    [Theory]
    [InlineData("https://exam.example.test/exams")]
    [InlineData("https://exam.example.test/signin-oidc")]
    [InlineData("https://login.microsoftonline.com/tenant/oauth2/v2.0/authorize")]
    public void IsAllowed_WithApprovedOrigin_ReturnsTrue(string target)
    {
        Assert.True(policy.IsAllowed(target));
    }

    [Theory]
    [InlineData("http://exam.example.test/exams")]
    [InlineData("https://evil.example.test/")]
    [InlineData("https://exam.example.test.evil.test/")]
    [InlineData("https://login.microsoftonline.com.evil.test/")]
    [InlineData("not-a-url")]
    public void IsAllowed_WithUnapprovedOrigin_ReturnsFalse(string target)
    {
        Assert.False(policy.IsAllowed(target));
    }

    [Theory]
    [InlineData("https://exam.example.test/exams", true)]
    [InlineData("https://login.microsoftonline.com/", false)]
    [InlineData("https://evil.example.test/", false)]
    public void IsTrustedMessageSource_RequiresWebAppOrigin(string source, bool expected)
    {
        Assert.Equal(expected, policy.IsTrustedMessageSource(source));
    }
}
