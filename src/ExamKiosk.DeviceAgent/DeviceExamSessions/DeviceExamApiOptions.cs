namespace ExamKiosk.DeviceAgent.DeviceExamSessions;

public sealed class DeviceExamApiOptions
{
    public const string SectionName = "DeviceExamApi";

    public string BaseUrl { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string ClientId { get; init; } = string.Empty;
    public string CertificateThumbprint { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
}
