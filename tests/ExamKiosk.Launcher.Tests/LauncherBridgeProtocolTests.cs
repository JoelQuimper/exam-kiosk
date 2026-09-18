using System.Text.Json;
using ExamKiosk.Contracts;
using ExamKiosk.Launcher;

namespace ExamKiosk.Launcher.Tests;

public sealed class LauncherBridgeProtocolTests
{
    [Theory]
    [InlineData("clientReady", LauncherBridgeRequestType.ClientReady)]
    [InlineData("startExam", LauncherBridgeRequestType.StartExam)]
    public void TryParseRequest_WithValidRequest_ReturnsRequest(
        string type,
        LauncherBridgeRequestType expectedType)
    {
        var requestId = Guid.NewGuid();
        var examTitleProperty = type == "startExam"
            ? $",\"exam\":{{\"title\":\"Mathematics 101\",\"profile\":{CreateProfileJson()}}}"
            : string.Empty;
        var json = $$"""{"version":{{LauncherBridgeProtocol.Version}},"type":"{{type}}","requestId":"{{requestId}}"{{examTitleProperty}}}""";

        var parsed = LauncherBridgeProtocol.TryParseRequest(json, out var request);

        Assert.True(parsed);
        Assert.Equal(expectedType, request?.Type);
        Assert.Equal(requestId, request?.RequestId);
        Assert.Equal(
            type == "startExam" ? "Mathematics 101" : null,
            request?.Exam?.Title);
        Assert.Equal(
            type == "startExam" ? "assignment-1" : null,
            request?.Exam?.Profile.AssignmentId);
    }

    [Theory]
    [InlineData("""{"version":1,"type":"clientReady","requestId":"4a3f8fb6-2735-4b41-9d61-f976129eef20"}""")]
    [InlineData("""{"version":2,"type":"finishExam","requestId":"4a3f8fb6-2735-4b41-9d61-f976129eef20"}""")]
    [InlineData("""{"version":2,"type":"startExam","requestId":"not-a-guid"}""")]
    [InlineData("""{"version":2,"type":"startExam","requestId":"00000000-0000-0000-0000-000000000000"}""")]
    [InlineData("""{"version":2,"type":"startExam","requestId":"4a3f8fb6-2735-4b41-9d61-f976129eef20"}""")]
    [InlineData("""{"version":2,"type":"startExam","requestId":"4a3f8fb6-2735-4b41-9d61-f976129eef20","exam":{"title":""}}""")]
    [InlineData("""{"version":2,"type":"startExam","requestId":"4a3f8fb6-2735-4b41-9d61-f976129eef20","exam":{"title":"Mathematics 101","profile":null}}""")]
    [InlineData("""{"version":2,"type":"startExam","requestId":"4a3f8fb6-2735-4b41-9d61-f976129eef20","exam":{"title":"Line\u000aBreak"}}""")]
    [InlineData("""{"version":2,"type":"startExam","requestId":"4a3f8fb6-2735-4b41-9d61-f976129eef20","exam":{"title":"Mathematics 101","command":"anything"}}""")]
    [InlineData("""{"version":2,"type":"startExam","requestId":"4a3f8fb6-2735-4b41-9d61-f976129eef20","command":"anything"}""")]
    [InlineData("""{"version":2,"type":"startExam"}""")]
    [InlineData("""not-json""")]
    public void TryParseRequest_WithInvalidRequest_ReturnsFalse(string json)
    {
        Assert.False(LauncherBridgeProtocol.TryParseRequest(json, out var request));
        Assert.Null(request);
    }

    [Fact]
    public void TryParseRequest_WithExamTitleOverLimit_ReturnsFalse()
    {
        var requestId = Guid.NewGuid();
        var examTitle = new string('a', LauncherBridgeProtocol.MaximumExamTitleLength + 1);
        var json = JsonSerializer.Serialize(new
        {
            version = LauncherBridgeProtocol.Version,
            type = "startExam",
            requestId,
            exam = new
            {
                title = examTitle,
                profile = JsonSerializer.Deserialize<JsonElement>(CreateProfileJson()),
            },
        });

        Assert.False(LauncherBridgeProtocol.TryParseRequest(json, out var request));
        Assert.Null(request);
    }

    [Fact]
    public void TryParseRequest_WhenProfileTitleDoesNotMatch_ReturnsFalse()
    {
        var requestId = Guid.NewGuid();
        var json = JsonSerializer.Serialize(new
        {
            version = LauncherBridgeProtocol.Version,
            type = "startExam",
            requestId,
            exam = new
            {
                title = "Different title",
                profile = JsonSerializer.Deserialize<JsonElement>(CreateProfileJson()),
            },
        });

        Assert.False(LauncherBridgeProtocol.TryParseRequest(json, out var request));
        Assert.Null(request);
    }

    private static string CreateProfileJson()
    {
        var profile = new EffectiveExamProfile(
            1,
            "assignment-1",
            new EffectiveStudent("student@example.com"),
            new EffectiveExam(
                "exam-1",
                "Mathematics 101",
                "calculator",
                new Uri("https://example.com/exam"),
                new WebLaunchTarget(
                    new Uri("https://example.com/exam"),
                    "Open exam",
                    true,
                    true)),
            [],
            new EffectiveEdgePolicy([], []),
            new EffectiveWindowsConfiguration(
                new WindowsClientVersion(10, 0, 22621),
                new AssignedAccessArtifact(
                    "windowsAssignedAccessXml",
                    "2022",
                    new AssignedAccessSource("generated", 1),
                    "utf-8",
                    "digest",
                    "<AssignedAccessConfiguration />"),
                []));

        return JsonSerializer.Serialize(profile, AgentProtocol.SerializerOptions);
    }
}
