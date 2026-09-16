using ExamKiosk.Contracts;
using ExamKiosk.Launcher;

namespace ExamKiosk.Launcher.Tests;

public sealed class LauncherConfigurationTests
{
    [Fact]
    public void Parse_WithHttpsOrigin_DerivesLauncherUriAndOrigin()
    {
        var configuration = WebViewHostConfiguration.Parse(
            """{"webAppUrl":"https://app-examkiosk-dev.azurewebsites.net"}""",
            "/launcher");

        Assert.Equal(
            "https://app-examkiosk-dev.azurewebsites.net/launcher",
            configuration.PageUri.AbsoluteUri);
        Assert.Equal(
            "https://app-examkiosk-dev.azurewebsites.net",
            configuration.TrustedOrigin);
    }

    [Theory]
    [InlineData("""{"webAppUrl":"http://example.test"}""")]
    [InlineData("""{"webAppUrl":"https://user@example.test"}""")]
    [InlineData("""{"webAppUrl":"https://example.test/path"}""")]
    [InlineData("""{"webAppUrl":"https://example.test?query=true"}""")]
    [InlineData("""{"webAppUrl":"https://example.test#fragment"}""")]
    [InlineData("""{"webAppUrl":"https://example.test","unexpected":true}""")]
    [InlineData("""{}""")]
    [InlineData("""not-json""")]
    public void Parse_WithInvalidConfiguration_Throws(string json)
    {
        Assert.Throws<InvalidDataException>(
            () => WebViewHostConfiguration.Parse(json, "/launcher"));
    }
}
