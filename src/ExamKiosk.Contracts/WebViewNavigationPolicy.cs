namespace ExamKiosk.Contracts;

public sealed class WebViewNavigationPolicy
{
    private readonly WebViewHostConfiguration configuration;
    private readonly IReadOnlyList<Uri> additionalAllowedOrigins;

    public WebViewNavigationPolicy(
        WebViewHostConfiguration configuration,
        IReadOnlyList<Uri>? additionalAllowedOrigins = null)
    {
        this.configuration = configuration;
        this.additionalAllowedOrigins = additionalAllowedOrigins ?? [];
    }

    public bool IsAllowed(string target)
    {
        return Uri.TryCreate(target, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && (HasSameOrigin(uri, configuration.WebAppBaseUri)
                || additionalAllowedOrigins.Any(origin => HasSameOrigin(uri, origin)));
    }

    public bool IsTrustedMessageSource(string source)
    {
        return Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && HasSameOrigin(uri, configuration.WebAppBaseUri);
    }

    private static bool HasSameOrigin(Uri left, Uri right)
    {
        return left.Scheme == right.Scheme
            && left.IdnHost.Equals(right.IdnHost, StringComparison.OrdinalIgnoreCase)
            && left.Port == right.Port;
    }
}
