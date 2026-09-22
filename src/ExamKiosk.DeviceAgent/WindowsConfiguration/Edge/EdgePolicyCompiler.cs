using ExamKiosk.DeviceAgent.WindowsConfiguration.Models;

namespace ExamKiosk.DeviceAgent.WindowsConfiguration.Edge;

internal static class EdgePolicyCompiler
{
    internal static EdgePolicyArtifact Compile(
        IReadOnlyList<string> allowedUrls)
    {
        ArgumentNullException.ThrowIfNull(allowedUrls);

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
            var isExternalProtocolFilter =
                allowedUrl?.EndsWith(":*", StringComparison.Ordinal) == true
                && Uri.CheckSchemeName(allowedUrl[..^2]);
            if (string.IsNullOrWhiteSpace(allowedUrl)
                || (!isWebUrl && !isExternalProtocolFilter))
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

        return new EdgePolicyArtifact(
            ["*"],
            urlAllowlist);
    }
}
