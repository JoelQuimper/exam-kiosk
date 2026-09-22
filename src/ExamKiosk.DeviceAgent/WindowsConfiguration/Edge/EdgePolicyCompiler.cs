using ExamKiosk.Contracts;
using ExamKiosk.DeviceAgent.WindowsConfiguration.Models;

namespace ExamKiosk.DeviceAgent.WindowsConfiguration.Edge;

internal static class EdgePolicyCompiler
{
    internal static EdgePolicyArtifact Compile(
        IReadOnlyList<string> allowedUrls,
        IReadOnlyList<ExternalProtocolLaunchRule> externalProtocolLaunchRules)
    {
        ArgumentNullException.ThrowIfNull(allowedUrls);
        ArgumentNullException.ThrowIfNull(externalProtocolLaunchRules);

        var urlAllowlist = new List<string>(allowedUrls.Count);
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var allowedUrl in allowedUrls)
        {
            var isWebUrl = Uri.TryCreate(
                    allowedUrl,
                    UriKind.Absolute,
                    out var uri)
                && (uri.Scheme == Uri.UriSchemeHttps
                    || uri.Scheme == Uri.UriSchemeHttp);
            if (string.IsNullOrWhiteSpace(allowedUrl)
                || !isWebUrl)
            {
                throw new WindowsConfigurationException(
                    $"Allowed URL filter '{allowedUrl}' is not supported.");
            }

            if (seenUrls.Add(allowedUrl))
            {
                urlAllowlist.Add(allowedUrl);
            }
        }

        if (urlAllowlist.Count == 0)
        {
            throw new WindowsConfigurationException(
                "At least one allowed URL is required to compile the Edge policy.");
        }

        var autoLaunchRules = externalProtocolLaunchRules
            .GroupBy(rule => ValidateProtocol(rule), StringComparer.Ordinal)
            .Select(group => new ExternalProtocolLaunchRule(
                group.Key,
                group
                    .SelectMany(rule => rule.AllowedOrigins)
                    .Select(ValidateOrigin)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()))
            .ToArray();
        foreach (var rule in autoLaunchRules)
        {
            if (rule.AllowedOrigins.Count == 0)
            {
                throw new WindowsConfigurationException(
                    $"External protocol '{rule.Protocol}' must allow at least one origin.");
            }

            var protocolFilter = $"{rule.Protocol}:*";
            if (seenUrls.Add(protocolFilter))
            {
                urlAllowlist.Add(protocolFilter);
            }
        }

        return new EdgePolicyArtifact(
            ["*"],
            urlAllowlist,
            autoLaunchRules);
    }

    private static string ValidateProtocol(ExternalProtocolLaunchRule rule)
    {
        if (rule is null
            || string.IsNullOrWhiteSpace(rule.Protocol)
            || rule.Protocol != rule.Protocol.ToLowerInvariant()
            || !Uri.CheckSchemeName(rule.Protocol)
            || rule.AllowedOrigins is null)
        {
            throw new WindowsConfigurationException(
                "An external protocol launch rule is invalid.");
        }

        return rule.Protocol;
    }

    private static string ValidateOrigin(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps
                && uri.Scheme != Uri.UriSchemeHttp)
            || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new WindowsConfigurationException(
                $"External protocol origin '{origin}' is invalid.");
        }

        return uri.GetLeftPart(UriPartial.Authority);
    }
}
