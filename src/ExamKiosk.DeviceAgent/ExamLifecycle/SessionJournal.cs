using System.Text.Json;
using ExamKiosk.Contracts;

namespace ExamKiosk.DeviceAgent.ExamLifecycle;

internal sealed class SessionJournal
{
    private readonly string journalPath;
    private readonly JsonSerializerOptions serializerOptions = new(JsonSerializerDefaults.Web);
    private JournalDocument? document;

    internal SessionJournal(string journalPath)
    {
        this.journalPath = journalPath;
        document = Read();
    }

    internal JournalDocument? Current => document;

    internal async Task BeginAsync(CancellationToken cancellationToken)
    {
        await BeginAsync(Guid.NewGuid(), null, null, null, cancellationToken);
    }

    internal async Task BeginAsync(
        Guid sessionId,
        string? profileSha256,
        string? examTitle,
        Uri? examEntryUrl,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        document = new JournalDocument(
            sessionId,
            profileSha256,
            null,
            examTitle,
            examEntryUrl,
            AgentState.EnteringExam,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            []);
        await WriteAsync(cancellationToken);
    }

    internal async Task SetAssignedAccessSha256Async(
        string assignedAccessSha256,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assignedAccessSha256);
        EnsureDocument();
        document = document! with
        {
            AssignedAccessSha256 = assignedAccessSha256,
            LastUpdatedAtUtc = DateTimeOffset.UtcNow
        };
        await WriteAsync(cancellationToken);
    }

    internal async Task SetStateAsync(
        AgentState state,
        CancellationToken cancellationToken)
    {
        EnsureDocument();
        document = document! with
        {
            State = state,
            LastUpdatedAtUtc = DateTimeOffset.UtcNow
        };
        await WriteAsync(cancellationToken);
    }

    internal async Task RecordStepAsync(
        string name,
        string status,
        string? error,
        CancellationToken cancellationToken)
    {
        EnsureDocument();
        var steps = document!.Steps.ToList();
        steps.Add(new JournalStep(name, status, DateTimeOffset.UtcNow, error));
        document = document with
        {
            Steps = steps,
            LastUpdatedAtUtc = DateTimeOffset.UtcNow
        };
        await WriteAsync(cancellationToken);
    }

    private void EnsureDocument()
    {
        document ??= new JournalDocument(
            Guid.NewGuid(),
            null,
            null,
            null,
            null,
            AgentState.Available,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            []);
    }

    private async Task WriteAsync(CancellationToken cancellationToken)
    {
        var temporaryPath = journalPath + ".tmp";
        var directory = Path.GetDirectoryName(journalPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(document, serializerOptions);
        await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
        File.Move(temporaryPath, journalPath, true);
    }

    private JournalDocument? Read()
    {
        try
        {
            if (!File.Exists(journalPath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<JournalDocument>(
                File.ReadAllText(journalPath),
                serializerOptions);
        }
        catch
        {
            return null;
        }
    }
}

internal sealed record JournalDocument(
    Guid SessionId,
    string? ProfileSha256,
    string? AssignedAccessSha256,
    string? ExamTitle,
    Uri? ExamEntryUrl,
    AgentState State,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset LastUpdatedAtUtc,
    IReadOnlyList<JournalStep> Steps);

internal sealed record JournalStep(
    string Name,
    string Status,
    DateTimeOffset AtUtc,
    string? Error);
