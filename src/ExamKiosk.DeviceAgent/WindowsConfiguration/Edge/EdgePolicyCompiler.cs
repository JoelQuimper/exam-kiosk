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
            if (string.IsNullOrWhiteSpace(allowedUrl)
                || !Uri.TryCreate(
                    allowedUrl,
                    UriKind.Absolute,
                    out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps
                    && uri.Scheme != Uri.UriSchemeHttp))
            {
                throw new WindowsConfigurationException(
                    $"Allowed URL '{allowedUrl}' is not an absolute HTTP or HTTPS URL.");
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
