using ExamKiosk.Contracts;

namespace ExamKiosk.Web.EdgePolicy.Validation;

public sealed class EdgePolicyConfigurationValidator
    : IEdgePolicyConfigurationValidator
{
    private const int MaximumWebDestinationsPerTool = 32;
    private const int MaximumUrlLength = 2048;

    public void Validate(IReadOnlyList<WebToolDefinition> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        foreach (var tool in tools)
        {
            ValidateWebTool(tool);
        }
    }

    private static void ValidateWebTool(WebToolDefinition tool)
    {
        var configuration = tool.Configuration;
        ValidateHttpsUrl(
            configuration.LaunchTarget.EntryUrl,
            $"Web tool '{tool.ToolId}' entry URL");
        if (configuration.EdgeAllowlist.Count is 0 or > MaximumWebDestinationsPerTool)
        {
            throw new InvalidOperationException(
                $"Web tool '{tool.ToolId}' must contain a bounded Edge allowlist.");
        }

        foreach (var destination in configuration.EdgeAllowlist)
        {
            if (string.IsNullOrWhiteSpace(destination)
                || destination.Length > MaximumUrlLength
                || !destination.StartsWith(
                    "https://",
                    StringComparison.OrdinalIgnoreCase)
                || destination.Any(char.IsControl))
            {
                throw new InvalidOperationException(
                    $"Web tool '{tool.ToolId}' contains an invalid Edge allowlist entry.");
            }
        }

        var exactEntryHostFilter =
            $"https://.{configuration.LaunchTarget.EntryUrl.Host}";
        if (!configuration.EdgeAllowlist.Contains(
                exactEntryHostFilter,
                StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Web tool '{tool.ToolId}' Edge allowlist does not contain its entry host.");
        }
    }

    private static void ValidateHttpsUrl(Uri url, string name)
    {
        if (!url.IsAbsoluteUri
            || url.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(url.Host)
            || !string.IsNullOrEmpty(url.UserInfo)
            || url.AbsoluteUri.Length > MaximumUrlLength)
        {
            throw new InvalidOperationException($"{name} is invalid.");
        }
    }
}
