using ExamKiosk.Contracts;

namespace ExamKiosk.Web.AssignedAccess.Validation;

public sealed class AssignedAccessConfigurationValidator
    : IAssignedAccessConfigurationValidator
{
    private const int MaximumTools = 16;
    private const int MaximumApplicationsPerTool = 16;
    private const int MaximumTextLength = 200;
    private const int MaximumUrlLength = 2048;

    public void Validate(
        EffectiveExam exam,
        IReadOnlyList<ToolDefinition> tools)
    {
        ArgumentNullException.ThrowIfNull(exam);
        ArgumentNullException.ThrowIfNull(tools);

        ValidateText(exam.Title, "exam title");
        ValidateWebLaunchTarget(exam.LaunchTarget, "exam");

        if (tools.Count > MaximumTools)
        {
            throw new InvalidOperationException(
                "The Assigned Access configuration contains too many tools.");
        }

        EnsureUnique(
            tools.Select(tool => tool.ToolId),
            "The Assigned Access configuration contains duplicate tool IDs.");

        foreach (var tool in tools)
        {
            ValidateTool(tool);
        }
    }

    private static void ValidateTool(ToolDefinition tool)
    {
        ValidateIdentifier(tool.ToolId, "tool ID");
        ValidateText(tool.DisplayName, "tool display name");
        ValidateText(tool.Icon, "tool icon");
        if (!tool.Enabled)
        {
            throw new InvalidOperationException(
                $"Assigned Access cannot include disabled tool '{tool.ToolId}'.");
        }

        switch (tool)
        {
            case DesktopToolDefinition desktop:
                ValidateDesktopTool(desktop);
                break;
            case WebToolDefinition web:
                ValidateWebLaunchTarget(
                    web.Configuration.LaunchTarget,
                    $"Web tool '{web.ToolId}'");
                break;
            default:
                throw new InvalidOperationException(
                    $"Tool '{tool.ToolId}' has an unsupported type.");
        }
    }

    private static void ValidateDesktopTool(DesktopToolDefinition tool)
    {
        var applications = tool.Configuration.Applications;
        if (applications.Count is 0 or > MaximumApplicationsPerTool)
        {
            throw new InvalidOperationException(
                $"Desktop tool '{tool.ToolId}' must contain a bounded application list.");
        }

        EnsureUnique(
            applications.Select(application => application.ApplicationId),
            $"Desktop tool '{tool.ToolId}' contains duplicate application IDs.");

        foreach (var application in applications)
        {
            ValidateText(application.ApplicationId, "application ID");
            switch (application)
            {
                case DesktopExecutableDefinition executable:
                    ValidateDesktopExecutable(tool.ToolId, executable);
                    break;
                case PackagedApplicationDefinition packaged:
                    ValidatePackagedApplication(tool.ToolId, packaged);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Desktop tool '{tool.ToolId}' has an unsupported application type.");
            }
        }

        var launchApplication = applications.SingleOrDefault(
            application => string.Equals(
                application.ApplicationId,
                tool.Configuration.LaunchTarget.ApplicationId,
                StringComparison.Ordinal));
        if (launchApplication is null
            || launchApplication.Role != ApplicationRole.Primary)
        {
            throw new InvalidOperationException(
                $"Desktop tool '{tool.ToolId}' shortcut must reference a primary application.");
        }

        ValidateText(
            tool.Configuration.LaunchTarget.Label,
            "launch target label");
        ValidatePinPlacement(
            tool.Configuration.LaunchTarget.PinToStart,
            tool.Configuration.LaunchTarget.PinToTaskbar,
            tool.ToolId);
    }

    private static void ValidateDesktopExecutable(
        string toolId,
        DesktopExecutableDefinition executable)
    {
        if (string.IsNullOrWhiteSpace(executable.Path)
            || executable.Path.Length > MaximumUrlLength
            || executable.Path.Any(char.IsControl)
            || (!Path.IsPathFullyQualified(executable.Path)
                && !executable.Path.StartsWith(
                    "%ProgramFiles%\\",
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Desktop tool '{toolId}' contains an invalid executable path.");
        }

        var validation = executable.Validation;
        if (string.IsNullOrWhiteSpace(validation.PublisherSubject)
            && string.IsNullOrWhiteSpace(validation.Sha256))
        {
            throw new InvalidOperationException(
                $"Desktop tool '{toolId}' executable validation metadata is required.");
        }

        if (validation.Sha256 is not null
            && (validation.Sha256.Length != 64
                || validation.Sha256.Any(character => !Uri.IsHexDigit(character))))
        {
            throw new InvalidOperationException(
                $"Desktop tool '{toolId}' contains an invalid SHA-256 hash.");
        }
    }

    private static void ValidatePackagedApplication(
        string toolId,
        PackagedApplicationDefinition packaged)
    {
        if (string.IsNullOrWhiteSpace(packaged.AppUserModelId)
            || packaged.AppUserModelId.Length > MaximumTextLength
            || packaged.AppUserModelId.Any(char.IsWhiteSpace)
            || !packaged.AppUserModelId.Contains('!'))
        {
            throw new InvalidOperationException(
                $"Desktop tool '{toolId}' contains an invalid App User Model ID.");
        }
    }

    private static void ValidateWebLaunchTarget(
        WebLaunchTarget launchTarget,
        string owner)
    {
        ValidateHttpsUrl(launchTarget.EntryUrl, $"{owner} entry URL");
        ValidateText(launchTarget.Label, "launch target label");
        ValidatePinPlacement(
            launchTarget.PinToStart,
            launchTarget.PinToTaskbar,
            owner);
    }

    private static void ValidateHttpsUrl(Uri url, string name)
    {
        if (!url.IsAbsoluteUri
            || url.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(url.Host)
            || !string.IsNullOrEmpty(url.UserInfo)
            || url.AbsoluteUri.Length > MaximumUrlLength)
        {
            throw new InvalidOperationException($"{name} is invalid.");
        }
    }

    private static void ValidateText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumTextLength
            || value.Any(char.IsControl))
        {
            throw new InvalidOperationException($"The {name} is invalid.");
        }
    }

    private static void ValidateIdentifier(string value, string name)
    {
        ValidateText(value, name);
        if (value.Any(
                character => !char.IsAsciiLetterOrDigit(character)
                    && character is not '-' and not '_'))
        {
            throw new InvalidOperationException(
                $"The {name} contains unsupported characters.");
        }
    }

    private static void EnsureUnique(
        IEnumerable<string> values,
        string errorMessage)
    {
        var entries = values.ToArray();
        if (entries.Distinct(StringComparer.Ordinal).Count() != entries.Length)
        {
            throw new InvalidOperationException(errorMessage);
        }
    }

    private static void ValidatePinPlacement(
        bool pinToStart,
        bool pinToTaskbar,
        string owner)
    {
        if (!pinToStart && !pinToTaskbar)
        {
            throw new InvalidOperationException(
                $"{owner} launch target must be pinned to Start, the taskbar, or both.");
        }
    }
}
