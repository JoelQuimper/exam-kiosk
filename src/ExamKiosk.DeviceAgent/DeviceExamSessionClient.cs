using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace ExamKiosk.DeviceAgent;

public interface IDeviceExamSessionClient
{
    Task ActivateAsync(
        Guid sessionId,
        string profileSha256,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        Guid sessionId,
        string profileSha256,
        CancellationToken cancellationToken);
}

public sealed class DeviceExamSessionClient(
    HttpClient httpClient,
    IOptions<DeviceExamApiOptions> options,
    ILogger<DeviceExamSessionClient> logger) : IDeviceExamSessionClient
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly DeviceExamApiOptions options = options.Value;
    private IConfidentialClientApplication? confidentialClient;

    public Task ActivateAsync(
        Guid sessionId,
        string profileSha256,
        CancellationToken cancellationToken) =>
        PostTransitionAsync(
            sessionId,
            "activate",
            profileSha256,
            cancellationToken);

    public Task CompleteAsync(
        Guid sessionId,
        string profileSha256,
        CancellationToken cancellationToken) =>
        PostTransitionAsync(
            sessionId,
            "complete",
            profileSha256,
            cancellationToken);

    private async Task PostTransitionAsync(
        Guid sessionId,
        string operation,
        string profileSha256,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileSha256);
        ValidateOptions();

        var application = confidentialClient ??= CreateConfidentialClient();
        var authentication = await application
            .AcquireTokenForClient([options.Scope])
            .ExecuteAsync(cancellationToken);
        logger.LogInformation(
            "Acquired Device Agent access token for session {SessionId} operation {Operation}; expires at {ExpiresOnUtc}; source {TokenSource}",
            sessionId,
            operation,
            authentication.ExpiresOn,
            authentication.AuthenticationResultMetadata.TokenSource);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(
                new Uri(options.BaseUrl, UriKind.Absolute),
                $"api/v1/device-exam-sessions/{sessionId:D}/{operation}"));
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", authentication.AccessToken);
        request.Content = JsonContent.Create(
            new DeviceExamTransitionRequest(profileSha256),
            options: SerializerOptions);

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(
                "The backend did not recognize the local exam session.");
        }
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new InvalidOperationException(
                "The backend exam session is not in the expected state.");
        }

        response.EnsureSuccessStatusCode();
        logger.LogInformation(
            "The exam-session API accepted operation {Operation} for session {SessionId}",
            operation,
            sessionId);
    }

    private IConfidentialClientApplication CreateConfidentialClient()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadOnly);
        var matches = store.Certificates.Find(
            X509FindType.FindByThumbprint,
            options.CertificateThumbprint,
            validOnly: false);
        var usableCertificates = matches
            .Where(
                certificate =>
                    certificate.HasPrivateKey
                    && certificate.NotBefore <= DateTime.Now
                    && certificate.NotAfter > DateTime.Now)
            .ToArray();
        if (usableCertificates.Length != 1)
        {
            throw new InvalidOperationException(
                "The configured Device Agent certificate was not found or was not uniquely identifiable.");
        }

        return ConfidentialClientApplicationBuilder
            .Create(options.ClientId)
            .WithAuthority(
                AzureCloudInstance.AzurePublic,
                options.TenantId)
            .WithCertificate(usableCertificates[0])
            .Build();
    }

    private void ValidateOptions()
    {
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri)
            || baseUri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(options.TenantId)
            || string.IsNullOrWhiteSpace(options.ClientId)
            || string.IsNullOrWhiteSpace(options.CertificateThumbprint)
            || string.IsNullOrWhiteSpace(options.Scope))
        {
            throw new InvalidOperationException(
                "The Device Exam API configuration is incomplete or invalid.");
        }
    }

    private sealed record DeviceExamTransitionRequest(string ProfileSha256);
}
