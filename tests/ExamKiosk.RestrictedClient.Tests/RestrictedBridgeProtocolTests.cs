using ExamKiosk.RestrictedClient;

namespace ExamKiosk.RestrictedClient.Tests;

public sealed class RestrictedBridgeProtocolTests
{
    [Theory]
    [InlineData("clientReady", RestrictedBridgeRequestType.ClientReady)]
    [InlineData("finishExam", RestrictedBridgeRequestType.FinishExam)]
    public void TryParseRequest_WithValidRequest_ReturnsRequest(
        string type,
        RestrictedBridgeRequestType expectedType)
    {
        var requestId = Guid.NewGuid();
        var json = $$"""{"version":2,"type":"{{type}}","requestId":"{{requestId}}"}""";

        var parsed = RestrictedBridgeProtocol.TryParseRequest(json, out var request);

        Assert.True(parsed);
        Assert.Equal(expectedType, request?.Type);
        Assert.Equal(requestId, request?.RequestId);
        Assert.Null(request?.SessionId);
    }

    [Fact]
    public void TryParseRequest_WithOpenExamSession_ReturnsRequest()
    {
        var requestId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var json = $$"""{"version":2,"type":"openExam","requestId":"{{requestId}}","sessionId":"{{sessionId}}"}""";

        var parsed = RestrictedBridgeProtocol.TryParseRequest(json, out var request);

        Assert.True(parsed);
        Assert.Equal(RestrictedBridgeRequestType.OpenExam, request?.Type);
        Assert.Equal(requestId, request?.RequestId);
        Assert.Equal(sessionId, request?.SessionId);
    }

    [Theory]
    [InlineData("""{"version":1,"type":"clientReady","requestId":"4a3f8fb6-2735-4b41-9d61-f976129eef20"}""")]
    [InlineData("""{"version":2,"type":"startExam","requestId":"4a3f8fb6-2735-4b41-9d61-f976129eef20"}""")]
    [InlineData("""{"version":2,"type":"openExam","requestId":"4a3f8fb6-2735-4b41-9d61-f976129eef20","url":"https://evil.test"}""")]
    [InlineData("""{"version":2,"type":"openExam","requestId":"4a3f8fb6-2735-4b41-9d61-f976129eef20"}""")]
    [InlineData("""{"version":2,"type":"clientReady","requestId":"4a3f8fb6-2735-4b41-9d61-f976129eef20","sessionId":"640073fd-54c1-4a3e-baf7-b867cb87cd80"}""")]
    [InlineData("""{"version":2,"type":"finishExam","requestId":"not-a-guid"}""")]
    [InlineData("""{"version":2,"type":"finishExam"}""")]
    [InlineData("""not-json""")]
    public void TryParseRequest_WithInvalidRequest_ReturnsFalse(string json)
    {
        Assert.False(RestrictedBridgeProtocol.TryParseRequest(json, out var request));
        Assert.Null(request);
    }
}
