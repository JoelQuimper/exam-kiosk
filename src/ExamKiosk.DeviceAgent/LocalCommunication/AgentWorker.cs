using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using ExamKiosk.Contracts;
using ExamKiosk.DeviceAgent.ExamLifecycle;
using Microsoft.Win32.SafeHandles;

namespace ExamKiosk.DeviceAgent.LocalCommunication;

public sealed class AgentWorker(
    TransitionManager transitionManager,
    ILogger<AgentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await transitionManager.InitializeAsync(stoppingToken);
        logger.LogInformation("Exam Device Agent listening on pipe {PipeName}", AgentProtocol.PipeName);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = CreatePipe();
                await pipe.WaitForConnectionAsync(stoppingToken);
                await ProcessConnectionAsync(pipe, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unhandled error while processing an agent request");
            }
        }
    }

    private async Task ProcessConnectionAsync(
        NamedPipeServerStream pipe,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true)
        {
            AutoFlush = true
        };

        AgentRequest? request = null;
        AgentResponse response;

        try
        {
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            var requestJson = await ReadBoundedLineAsync(pipe, requestTimeout.Token);
            request = JsonSerializer.Deserialize<AgentRequest>(
                requestJson,
                AgentProtocol.SerializerOptions);

            if (request is null)
            {
                throw new InvalidDataException("The request body is invalid.");
            }

            if (!AgentProtocol.TryValidateRequest(request, out var validationError))
            {
                response = new AgentResponse(
                    AgentProtocol.Version,
                    request.RequestId,
                    false,
                    transitionManager.CurrentState,
                    validationError!);
            }
            else if (!TryAuthorizeClient(pipe, request.Command, out var clientName))
            {
                logger.LogWarning(
                    "Rejected command {Command} from unauthorized pipe client {ClientName}",
                    request.Command,
                    clientName);
                response = new AgentResponse(
                    AgentProtocol.Version,
                    request.RequestId,
                    false,
                    transitionManager.CurrentState,
                    "The client is not authorized to perform this operation.");
            }
            else
            {
                response = await transitionManager.HandleAsync(request, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            logger.LogWarning(exception, "Rejected an invalid agent request");
            response = new AgentResponse(
                AgentProtocol.Version,
                request?.RequestId ?? Guid.Empty,
                false,
                transitionManager.CurrentState,
                exception.Message);
        }

        var responseJson = JsonSerializer.Serialize(response, AgentProtocol.SerializerOptions);
        await writer.WriteLineAsync(responseJson.AsMemory(), cancellationToken);
    }

    private static bool TryAuthorizeClient(
        NamedPipeServerStream pipe,
        AgentCommand command,
        out string clientName)
    {
        clientName = "unknown";
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var processId))
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            var executablePath = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return false;
            }

            clientName = Path.GetFileName(executablePath);
            var installRoot = new DirectoryInfo(AppContext.BaseDirectory).Parent;
            if (installRoot is null)
            {
                return false;
            }

            return ClientCommandAuthorizer.IsAuthorized(
                executablePath,
                process.SessionId,
                installRoot.FullName,
                command);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static NamedPipeServerStream CreatePipe()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            AgentProtocol.PipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough,
            4096,
            4096,
            security);
    }

    private static async Task<string> ReadBoundedLineAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        using var message = new MemoryStream();

        while (message.Length <= AgentProtocol.MaximumMessageLength)
        {
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0 || buffer[0] == (byte)'\n')
            {
                break;
            }

            if (buffer[0] != (byte)'\r')
            {
                message.WriteByte(buffer[0]);
            }
        }

        if (message.Length == 0 || message.Length > AgentProtocol.MaximumMessageLength)
        {
            throw new InvalidDataException("The request is empty or exceeds the size limit.");
        }

        return Encoding.UTF8.GetString(message.ToArray());
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(
        SafePipeHandle pipe,
        out uint clientProcessId);
}