using System.Text.Json;

namespace ExamKiosk.Launcher;

public enum LauncherBridgeRequestType
{
    ClientReady,
    StartExam,
}

public sealed record LauncherBridgeRequest(
    LauncherBridgeRequestType Type,
    Guid RequestId);

public static class LauncherBridgeProtocol
{
    public const int Version = 1;
    public const int MaximumMessageLength = 4096;

    public static bool TryParseRequest(
        string json,
        out LauncherBridgeRequest? request)
    {
        request = null;
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumMessageLength)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var properties = root.EnumerateObject().ToArray();
            if (properties.Length != 3
                || properties.Select(property => property.Name).Distinct().Count() != 3
                || !root.TryGetProperty("version", out var version)
                || !version.TryGetInt32(out var versionValue)
                || versionValue != Version
                || !root.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("requestId", out var requestId)
                || requestId.ValueKind != JsonValueKind.String
                || !Guid.TryParse(requestId.GetString(), out var requestIdValue)
                || requestIdValue == Guid.Empty)
            {
                return false;
            }

            var requestType = type.GetString() switch
            {
                "clientReady" => LauncherBridgeRequestType.ClientReady,
                "startExam" => LauncherBridgeRequestType.StartExam,
                _ => (LauncherBridgeRequestType?)null,
            };
            if (requestType is null)
            {
                return false;
            }

            request = new LauncherBridgeRequest(requestType.Value, requestIdValue);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
