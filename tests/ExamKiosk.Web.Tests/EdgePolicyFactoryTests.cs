using ExamKiosk.Contracts;
using ExamKiosk.Web.EdgePolicy;
using ExamKiosk.Web.EdgePolicy.Validation;

namespace ExamKiosk.Web.Tests;

public sealed class EdgePolicyFactoryTests
{
    private readonly EdgePolicyFactory factory =
        new(new EdgePolicyConfigurationValidator());

    [Fact]
    public void Create_MergesBaselineAndToolAllowlistWithoutDuplicates()
    {
        var policy = factory.Create(
            [
                WebTool(
                    "dictionary",
                    "https://dictionary.example/",
                    [
                        "https://.dictionary.example",
                        "https://.LOGIN.MICROSOFTONLINE.COM",
                    ]),
            ]);

        Assert.Equal(["*"], policy.UrlBlocklist);
        Assert.Equal(
            [
                "https://.login.microsoftonline.com",
                "https://.jqdev.sharepoint.com/sites/ExamSite",
                "https://.dictionary.example",
            ],
            policy.UrlAllowlist);
    }

    [Fact]
    public void Create_WhenWebEntryHostIsNotAllowlisted_Throws()
    {
        var tool = WebTool(
            "dictionary",
            "https://dictionary.example/",
            ["https://.other.example"]);

        var exception = Assert.Throws<InvalidOperationException>(
            () => factory.Create([tool]));

        Assert.Contains("does not contain its entry host", exception.Message);
    }

    [Fact]
    public void Create_WhenWebEntryUrlIsNotHttps_Throws()
    {
        var tool = WebTool(
            "dictionary",
            "http://dictionary.example/",
            ["https://.dictionary.example"]);

        var exception = Assert.Throws<InvalidOperationException>(
            () => factory.Create([tool]));

        Assert.Contains("entry URL is invalid", exception.Message);
    }

    private static WebToolDefinition WebTool(
        string toolId,
        string entryUrl,
        IReadOnlyList<string> allowlist) =>
        new(
            toolId,
            "Dictionary",
            "dictionary",
            true,
            new WebToolConfiguration(
                allowlist,
                new WebLaunchTarget(
                    new Uri(entryUrl),
                    "Dictionary",
                    true,
                    true)));
}
