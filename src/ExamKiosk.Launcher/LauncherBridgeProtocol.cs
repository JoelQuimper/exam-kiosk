using System.Text.Json;

namespace ExamKiosk.Launcher;

public enum LauncherBridgeRequestType
{
    ClientReady,
    StartExam,
}

public sealed record LauncherBridgeRequest(
    LauncherBridgeRequestType Type,
    Guid RequestId,
    LauncherExamDescriptor? Exam);

public sealed record LauncherExamDescriptor(string Title);

public static class LauncherBridgeProtocol
{
    public const int Version = 1;
    public const int MaximumMessageLength = 4096;
    public const int MaximumExamTitleLength = 200;

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
            if (properties.Select(property => property.Name).Distinct().Count() != properties.Length
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

            string? examTitle = null;
            if (requestType == LauncherBridgeRequestType.StartExam)
            {
                if (properties.Length != 4
                    || !root.TryGetProperty("exam", out var examElement)
                    || examElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                var examProperties = examElement.EnumerateObject().ToArray();
                if (examProperties.Length != 1
                    || !examElement.TryGetProperty("title", out var examTitleElement)
                    || examTitleElement.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                examTitle = examTitleElement.GetString();
                if (string.IsNullOrWhiteSpace(examTitle)
                    || examTitle.Length > MaximumExamTitleLength
                    || examTitle.Any(char.IsControl))
                {
                    return false;
                }
            }
            else if (properties.Length != 3)
            {
                return false;
            }

            request = new LauncherBridgeRequest(
                requestType.Value,
                requestIdValue,
                examTitle is null ? null : new LauncherExamDescriptor(examTitle));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
