using System.Diagnostics;
using System.Text.Json;

namespace ExamKiosk.Contracts;

public sealed class WebViewDiagnosticLog
{
    private const long MaximumLogLength = 5 * 1024 * 1024;
    private readonly object sync = new();
    private readonly string component;
    private readonly string logPath;

    public WebViewDiagnosticLog(string component)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(component);
        if (component.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
        {
            throw new ArgumentException(
                "The diagnostic component name may contain only ASCII letters, digits, and hyphens.",
                nameof(component));
        }

        this.component = component;
        logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ExamKiosk",
            "Logs",
            $"{component}.jsonl");
    }

    public void Write(string eventName, object? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

        var entry = JsonSerializer.Serialize(new
        {
            timestampUtc = DateTimeOffset.UtcNow,
            component,
            eventName,
            details,
        });

        try
        {
            lock (sync)
            {
                var directory = Path.GetDirectoryName(logPath)
                    ?? throw new InvalidOperationException("The diagnostic log path has no directory.");
                Directory.CreateDirectory(directory);
                RotateIfNeeded();
                File.AppendAllText(logPath, entry + Environment.NewLine);
            }
        }
        catch (IOException exception)
        {
            Trace.TraceError($"Exam Kiosk diagnostic logging failed: {exception}");
        }
        catch (UnauthorizedAccessException exception)
        {
            Trace.TraceError($"Exam Kiosk diagnostic logging failed: {exception}");
        }
    }

    public static string DescribeUri(string? target)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            return "<invalid-uri>";
        }

        return uri.GetLeftPart(UriPartial.Authority) + uri.AbsolutePath;
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(logPath) || new FileInfo(logPath).Length < MaximumLogLength)
        {
            return;
        }

        var previousLogPath = logPath + ".previous";
        File.Move(logPath, previousLogPath, overwrite: true);
    }
}
