using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExamKiosk.Contracts;

public static class AgentProtocol
{
    public const string PipeName = "ExamKiosk.DeviceAgent.v1";
    public const int Version = 1;
    public const int MaximumMessageLength = 4096;

    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}

public enum AgentCommand
{
    GetStatus,
    StartExam,
    FinishExam
}

public enum AgentState
{
    Available,
    EnteringExam,
    InExam,
    ExitingExam,
    Failed
}

public sealed record AgentRequest(
    int ProtocolVersion,
    Guid RequestId,
    AgentCommand Command);

public sealed record AgentResponse(
    int ProtocolVersion,
    Guid RequestId,
    bool Success,
    AgentState State,
    string Message,
    DateTimeOffset? RestartAtUtc = null);

public static class AgentClient
{
    public static async Task<AgentResponse> SendAsync(
        AgentCommand command,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var request = new AgentRequest(AgentProtocol.Version, Guid.NewGuid(), command);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        await using var pipe = new NamedPipeClientStream(
            ".",
            AgentProtocol.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await pipe.ConnectAsync(timeoutSource.Token);
        await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, leaveOpen: true);

        var requestJson = JsonSerializer.Serialize(request, AgentProtocol.SerializerOptions);
        await writer.WriteLineAsync(requestJson.AsMemory(), timeoutSource.Token);

        var responseJson = await reader.ReadLineAsync(timeoutSource.Token);
        if (string.IsNullOrWhiteSpace(responseJson) || responseJson.Length > AgentProtocol.MaximumMessageLength)
        {
            throw new InvalidDataException("The Exam Device Agent returned an invalid response.");
        }

        var response = JsonSerializer.Deserialize<AgentResponse>(
            responseJson,
            AgentProtocol.SerializerOptions);

        if (response is null ||
            response.ProtocolVersion != AgentProtocol.Version ||
            response.RequestId != request.RequestId)
        {
            throw new InvalidDataException("The Exam Device Agent response did not match the request.");
        }

        return response;
    }
}