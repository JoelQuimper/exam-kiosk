using ExamKiosk.Contracts;

namespace ExamKiosk.DeviceAgent.WindowsConfiguration.Models;

public sealed record EdgePolicyArtifact(
    IReadOnlyList<string> UrlBlocklist,
    IReadOnlyList<string> UrlAllowlist,
    IReadOnlyList<ExternalProtocolLaunchRule> AutoLaunchProtocolsFromOrigins);
