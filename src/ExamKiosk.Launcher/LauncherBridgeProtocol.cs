using System.Text.Json;
using ExamKiosk.Contracts;

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

public sealed record LauncherExamDescriptor(
    string Title,
    EffectiveExamProfile Profile);

public static class LauncherBridgeProtocol
{
    public const int Version = 2;
    public const int MaximumMessageLength = 262144;
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
            EffectiveExamProfile? profile = null;
            if (requestType == LauncherBridgeRequestType.StartExam)
            {
                if (properties.Length != 4
                    || !root.TryGetProperty("exam", out var examElement)
                    || examElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                var examProperties = examElement.EnumerateObject().ToArray();
                if (examProperties.Length != 2
                    || !examElement.TryGetProperty("title", out var examTitleElement)
                    || examTitleElement.ValueKind != JsonValueKind.String
                    || !examElement.TryGetProperty("profile", out var profileElement)
                    || profileElement.ValueKind != JsonValueKind.Object)
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

                profile = profileElement.Deserialize<EffectiveExamProfile>(
                    AgentProtocol.SerializerOptions);
                if (profile is null
                    || profile.SchemaVersion != 1
                    || string.IsNullOrWhiteSpace(profile.AssignmentId)
                    || profile.Student is null
                    || profile.Exam is null
                    || profile.Tools is null
                    || profile.EdgePolicy is null
                    || profile.WindowsConfiguration is null
                    || !string.Equals(
                        profile.Exam.Title,
                        examTitle,
                        StringComparison.Ordinal))
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
                examTitle is null
                    ? null
                    : new LauncherExamDescriptor(examTitle, profile!));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }
}
