using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExamKiosk.Contracts;

public static class AgentProtocol
{
    public const string PipeName = "ExamKiosk.DeviceAgent.v4";
    public const int Version = 4;
    public const int MaximumMessageLength = 262144;

    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static bool TryValidateRequest(
        AgentRequest request,
        out string? error)
    {
        error = null;
        if (request.ProtocolVersion != Version)
        {
            error = "The client protocol version is not supported.";
            return false;
        }
        if (request.RequestId == Guid.Empty)
        {
            error = "The request ID is invalid.";
            return false;
        }
        if (!Enum.IsDefined(request.Command))
        {
            error = "The requested command is not supported.";
            return false;
        }

        if (request.Command != AgentCommand.StartExam)
        {
            if (request.StartExam is not null)
            {
                error = "Only StartExam can contain a start payload.";
                return false;
            }

            return true;
        }

        var payload = request.StartExam;
        if (payload is null
            || payload.SessionId == Guid.Empty
            || payload.Profile is null
            || payload.Profile.SchemaVersion != 1
            || string.IsNullOrWhiteSpace(payload.Profile.AssignmentId)
            || payload.Profile.Student is null
            || string.IsNullOrWhiteSpace(
                payload.Profile.Student.UserPrincipalName)
            || payload.Profile.Exam is null
            || string.IsNullOrWhiteSpace(payload.Profile.Exam.Title)
            || payload.Profile.Tools is null
            || payload.Profile.EdgePolicy is null)
        {
            error = "The StartExam payload is invalid.";
            return false;
        }

        return true;
    }
}

public enum AgentCommand
{
    GetStatus,
    GetActiveExam,
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
    AgentCommand Command,
    AgentStartExamPayload? StartExam = null);

public sealed record AgentStartExamPayload(
    Guid SessionId,
    EffectiveExamProfile Profile);

public sealed record AgentResponse(
    int ProtocolVersion,
    Guid RequestId,
    bool Success,
    AgentState State,
    string Message,
    DateTimeOffset? RestartAtUtc = null,
    ActiveExamReference? ActiveExam = null);

public sealed record ActiveExamReference(
    Guid SessionId,
    string Title,
    Uri EntryUrl);

public static class AgentClient
{
    public static async Task<AgentResponse> SendAsync(
        AgentCommand command,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (command == AgentCommand.StartExam)
        {
            throw new ArgumentException(
                "StartExam requires a typed payload.",
                nameof(command));
        }

        var request = new AgentRequest(
            AgentProtocol.Version,
            Guid.NewGuid(),
            command);
        return await SendRequestAsync(request, timeout, cancellationToken);
    }

    public static Task<AgentResponse> StartExamAsync(
        AgentStartExamPayload payload,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var request = new AgentRequest(
            AgentProtocol.Version,
            Guid.NewGuid(),
            AgentCommand.StartExam,
            payload);
        return SendRequestAsync(request, timeout, cancellationToken);
    }

    private static async Task<AgentResponse> SendRequestAsync(
        AgentRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!AgentProtocol.TryValidateRequest(request, out var error))
        {
            throw new ArgumentException(error, nameof(request));
        }

        var requestJson = JsonSerializer.Serialize(request, AgentProtocol.SerializerOptions);
        if (Encoding.UTF8.GetByteCount(requestJson) > AgentProtocol.MaximumMessageLength)
        {
            throw new InvalidDataException(
                "The Exam Device Agent request exceeds the size limit.");
        }

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