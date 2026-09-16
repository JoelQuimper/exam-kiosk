using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExamKiosk.Contracts;

public sealed class WebViewHostConfiguration
{
    private WebViewHostConfiguration(Uri webAppBaseUri, string pagePath)
    {
        WebAppBaseUri = webAppBaseUri;
        PageUri = new Uri(webAppBaseUri, pagePath);
        TrustedOrigin = webAppBaseUri.GetLeftPart(UriPartial.Authority);
    }

    public Uri WebAppBaseUri { get; }

    public Uri PageUri { get; }

    public string TrustedOrigin { get; }

    public static WebViewHostConfiguration Load(
        string configurationFileName,
        string pagePath)
    {
        var path = Path.Combine(AppContext.BaseDirectory, configurationFileName);
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"WebView configuration was not found at '{path}'.");
        }

        return Parse(File.ReadAllText(path), pagePath);
    }

    public static WebViewHostConfiguration Parse(string json, string pagePath)
    {
        if (!pagePath.StartsWith('/') || pagePath.StartsWith("//", StringComparison.Ordinal))
        {
            throw new ArgumentException("The page path must be application-relative.", nameof(pagePath));
        }

        WebViewSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<WebViewSettings>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = false,
                    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                });
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("WebView configuration is not valid JSON.", exception);
        }

        if (settings is null
            || !Uri.TryCreate(settings.WebAppUrl, UriKind.Absolute, out var webAppUri)
            || webAppUri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrEmpty(webAppUri.Host)
            || !string.IsNullOrEmpty(webAppUri.UserInfo)
            || !string.IsNullOrEmpty(webAppUri.Query)
            || !string.IsNullOrEmpty(webAppUri.Fragment)
            || webAppUri.AbsolutePath != "/")
        {
            throw new InvalidDataException(
                "webAppUrl must be an absolute HTTPS origin without credentials, a path, query, or fragment.");
        }

        return new WebViewHostConfiguration(webAppUri, pagePath);
    }

    private sealed record WebViewSettings(
        [property: JsonPropertyName("webAppUrl")] string WebAppUrl);
}
