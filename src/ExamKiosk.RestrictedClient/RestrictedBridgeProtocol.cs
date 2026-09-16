using System.Text.Json;

namespace ExamKiosk.RestrictedClient;

public enum RestrictedBridgeRequestType
{
    ClientReady,
    OpenExam,
    FinishExam,
}

public sealed record RestrictedBridgeRequest(
    RestrictedBridgeRequestType Type,
    Guid RequestId);

public static class RestrictedBridgeProtocol
{
    public const int Version = 1;
    public const int MaximumMessageLength = 4096;

    public static bool TryParseRequest(
        string json,
        out RestrictedBridgeRequest? request)
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
                "clientReady" => RestrictedBridgeRequestType.ClientReady,
                "openExam" => RestrictedBridgeRequestType.OpenExam,
                "finishExam" => RestrictedBridgeRequestType.FinishExam,
                _ => (RestrictedBridgeRequestType?)null,
            };
            if (requestType is null)
            {
                return false;
            }

            request = new RestrictedBridgeRequest(requestType.Value, requestIdValue);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
