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
        var json = $$"""{"version":1,"type":"{{type}}","requestId":"{{requestId}}"}""";

        var parsed = LauncherBridgeProtocol.TryParseRequest(json, out var request);

        Assert.True(parsed);
        Assert.Equal(expectedType, request?.Type);
        Assert.Equal(requestId, request?.RequestId);
    }

    [Theory]
    [InlineData("""{"version":2,"type":"clientReady","requestId":"4a3f8fb6-2735-4b41-9d61-f976129eef20"}""")]
    [InlineData("""{"version":1,"type":"finishExam","requestId":"4a3f8fb6-2735-4b41-9d61-f976129eef20"}""")]
    [InlineData("""{"version":1,"type":"startExam","requestId":"not-a-guid"}""")]
    [InlineData("""{"version":1,"type":"startExam","requestId":"00000000-0000-0000-0000-000000000000"}""")]
    [InlineData("""{"version":1,"type":"startExam","requestId":"4a3f8fb6-2735-4b41-9d61-f976129eef20","command":"anything"}""")]
    [InlineData("""{"version":1,"type":"startExam"}""")]
    [InlineData("""not-json""")]
    public void TryParseRequest_WithInvalidRequest_ReturnsFalse(string json)
    {
        Assert.False(LauncherBridgeProtocol.TryParseRequest(json, out var request));
        Assert.Null(request);
    }
}
