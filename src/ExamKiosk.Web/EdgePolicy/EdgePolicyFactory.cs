using ExamKiosk.Contracts;
using ExamKiosk.Web.EdgePolicy.Validation;

namespace ExamKiosk.Web.EdgePolicy;

public sealed class EdgePolicyFactory(
    IEdgePolicyConfigurationValidator configurationValidator)
    : IEdgePolicyFactory
{
    private static readonly IReadOnlyList<string> BaselineAllowlist =
    [
        "https://.login.microsoftonline.com",
        "https://.jqdev.sharepoint.com/sites/ExamSite",
    ];

    public EffectiveEdgePolicy Create(IReadOnlyList<ToolDefinition> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        var webTools = tools.OfType<WebToolDefinition>().ToArray();
        configurationValidator.Validate(webTools);

        var allowlist = BaselineAllowlist
            .Concat(
                webTools.SelectMany(
                    tool => tool.Configuration.EdgeAllowlist))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new EffectiveEdgePolicy(["*"], allowlist);
    }
}
