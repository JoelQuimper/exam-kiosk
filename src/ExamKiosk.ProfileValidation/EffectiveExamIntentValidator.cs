using ExamKiosk.Contracts;

namespace ExamKiosk.ProfileValidation;

public static class EffectiveExamIntentValidator
{
    private const int SupportedSchemaVersion = 1;
    private const int MaximumTools = 16;
    private const int MaximumApplicationsPerTool = 16;
    private const int MaximumEdgeDestinationsPerTool = 32;
    private const int MaximumEffectiveEdgeDestinations =
        MaximumTools * MaximumEdgeDestinationsPerTool;
    private const int MaximumTextLength = 200;
    private const int MaximumUpnLength = 320;
    private const int MaximumUrlLength = 2048;

    public static void Validate(EffectiveExamProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Student is null
            || profile.Exam is null
            || profile.Tools is null
            || profile.EdgePolicy is null)
        {
            throw new ProfileValidationException(
                "The effective exam intent is incomplete.");
        }

        if (profile.SchemaVersion != SupportedSchemaVersion)
        {
            throw new ProfileValidationException(
                $"Effective profile schema version '{profile.SchemaVersion}' is not supported.");
        }

        ValidateIdentifier(profile.AssignmentId, "assignment ID");
        ValidateStudent(profile.Student);
        ValidateExam(profile.Exam);
        ValidateTools(profile.Tools);
        ValidateEdgePolicy(profile.EdgePolicy, profile.Tools);
    }

    private static void ValidateStudent(EffectiveStudent student)
    {
        var upn = student.UserPrincipalName;
        if (string.IsNullOrWhiteSpace(upn)
            || upn.Length > MaximumUpnLength
            || upn.Any(char.IsControl)
            || upn.IndexOf('@') <= 0
            || upn.EndsWith('@'))
        {
            throw new ProfileValidationException(
                "The student user principal name is invalid.");
        }
    }

    private static void ValidateExam(EffectiveExam exam)
    {
        ValidateIdentifier(exam.Id, "exam ID");
        ValidateText(exam.Title, "exam title");
        ValidateText(exam.Icon, "exam icon");
        ValidateHttpsUrl(exam.SharePointFolderUrl, "exam SharePoint folder URL");
        ValidateWebLaunchTarget(exam.LaunchTarget, "exam", requirePin: false);
        if (exam.SharePointFolderUrl != exam.LaunchTarget.EntryUrl)
        {
            throw new ProfileValidationException(
                "The exam launch URL does not match its SharePoint folder URL.");
        }
    }

    private static void ValidateTools(IReadOnlyList<ToolDefinition> tools)
    {
        if (tools.Count > MaximumTools)
        {
            throw new ProfileValidationException(
                "The effective exam intent contains too many tools.");
        }

        EnsureUnique(
            tools.Select(tool => tool?.ToolId),
            "The effective exam intent contains duplicate tool IDs.");

        foreach (var tool in tools)
        {
            if (tool is null)
            {
                throw new ProfileValidationException(
                    "The effective exam intent contains an empty tool.");
            }

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
            throw new ProfileValidationException(
                $"The effective exam intent cannot include disabled tool '{tool.ToolId}'.");
        }

        switch (tool)
        {
            case DesktopToolDefinition desktop:
                ValidateDesktopTool(desktop);
                break;
            case WebToolDefinition web:
                ValidateWebTool(web);
                break;
            default:
                throw new ProfileValidationException(
                    $"Tool '{tool.ToolId}' has an unsupported type.");
        }
    }

    private static void ValidateDesktopTool(DesktopToolDefinition tool)
    {
        var configuration = tool.Configuration;
        if (configuration is null
            || configuration.Applications is null
            || configuration.LaunchTarget is null)
        {
            throw new ProfileValidationException(
                $"Desktop tool '{tool.ToolId}' is incomplete.");
        }

        var applications = configuration.Applications;
        if (applications.Count is 0 or > MaximumApplicationsPerTool)
        {
            throw new ProfileValidationException(
                $"Desktop tool '{tool.ToolId}' must contain a bounded application list.");
        }

        EnsureUnique(
            applications.Select(application => application?.ApplicationId),
            $"Desktop tool '{tool.ToolId}' contains duplicate application IDs.");

        foreach (var application in applications)
        {
            if (application is null)
            {
                throw new ProfileValidationException(
                    $"Desktop tool '{tool.ToolId}' contains an empty application.");
            }

            ValidateApplication(tool.ToolId, application);
        }

        var launchApplication = applications.SingleOrDefault(
            application => string.Equals(
                application.ApplicationId,
                configuration.LaunchTarget.ApplicationId,
                StringComparison.Ordinal));
        if (launchApplication is null
            || launchApplication.Role != ApplicationRole.Primary)
        {
            throw new ProfileValidationException(
                $"Desktop tool '{tool.ToolId}' launch target must reference a primary application.");
        }

        ValidateText(configuration.LaunchTarget.Label, "launch target label");
        ValidatePinPlacement(
            configuration.LaunchTarget.PinToStart,
            configuration.LaunchTarget.PinToTaskbar,
            tool.ToolId);
    }

    private static void ValidateApplication(
        string toolId,
        ApplicationDefinition application)
    {
        ValidateText(application.ApplicationId, "application ID");
        switch (application)
        {
            case DesktopExecutableDefinition executable:
                ValidateDesktopExecutable(toolId, executable);
                break;
            case PackagedApplicationDefinition packaged:
                ValidatePackagedApplication(toolId, packaged);
                break;
            default:
                throw new ProfileValidationException(
                    $"Tool '{toolId}' has an unsupported application type.");
        }
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
            throw new ProfileValidationException(
                $"Desktop tool '{toolId}' contains an invalid executable path.");
        }

        var validation = executable.Validation;
        if (validation is null
            || (string.IsNullOrWhiteSpace(validation.PublisherSubject)
                && string.IsNullOrWhiteSpace(validation.Sha256)))
        {
            throw new ProfileValidationException(
                $"Desktop tool '{toolId}' executable validation metadata is required.");
        }

        if (validation.Sha256 is not null
            && (validation.Sha256.Length != 64
                || validation.Sha256.Any(character => !Uri.IsHexDigit(character))))
        {
            throw new ProfileValidationException(
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
            throw new ProfileValidationException(
                $"Desktop tool '{toolId}' contains an invalid App User Model ID.");
        }
    }

    private static void ValidateWebTool(WebToolDefinition tool)
    {
        var configuration = tool.Configuration;
        if (configuration is null
            || configuration.EdgeAllowlist is null
            || configuration.LaunchTarget is null)
        {
            throw new ProfileValidationException(
                $"Web tool '{tool.ToolId}' is incomplete.");
        }

        ValidateWebLaunchTarget(
            configuration.LaunchTarget,
            $"Web tool '{tool.ToolId}'");
        if (configuration.EdgeAllowlist.Count is 0 or > MaximumEdgeDestinationsPerTool)
        {
            throw new ProfileValidationException(
                $"Web tool '{tool.ToolId}' must contain a bounded Edge allowlist.");
        }

        foreach (var destination in configuration.EdgeAllowlist)
        {
            ValidateEdgeDestination(
                destination,
                $"Web tool '{tool.ToolId}' Edge allowlist entry");
        }

        var exactEntryHostFilter =
            $"https://.{configuration.LaunchTarget.EntryUrl.Host}";
        if (!configuration.EdgeAllowlist.Contains(
                exactEntryHostFilter,
                StringComparer.OrdinalIgnoreCase))
        {
            throw new ProfileValidationException(
                $"Web tool '{tool.ToolId}' Edge allowlist does not contain its entry host.");
        }
    }

    private static void ValidateEdgePolicy(
        EffectiveEdgePolicy edgePolicy,
        IReadOnlyList<ToolDefinition> tools)
    {
        if (edgePolicy.UrlBlocklist is null
            || edgePolicy.UrlAllowlist is null
            || edgePolicy.UrlBlocklist.Count != 1
            || !string.Equals(
                edgePolicy.UrlBlocklist[0],
                "*",
                StringComparison.Ordinal)
            || edgePolicy.UrlAllowlist.Count is 0 or > MaximumEffectiveEdgeDestinations)
        {
            throw new ProfileValidationException(
                "The effective Edge policy is invalid.");
        }

        EnsureUnique(
            edgePolicy.UrlAllowlist,
            "The effective Edge policy contains duplicate allowlist entries.",
            StringComparer.OrdinalIgnoreCase);
        foreach (var destination in edgePolicy.UrlAllowlist)
        {
            ValidateEdgeDestination(destination, "Edge allowlist entry");
        }

        foreach (var destination in tools
                     .OfType<WebToolDefinition>()
                     .SelectMany(tool => tool.Configuration.EdgeAllowlist))
        {
            if (!edgePolicy.UrlAllowlist.Contains(
                    destination,
                    StringComparer.OrdinalIgnoreCase))
            {
                throw new ProfileValidationException(
                    "The effective Edge policy omits a Web tool destination.");
            }
        }
    }

    private static void ValidateWebLaunchTarget(
        WebLaunchTarget launchTarget,
        string owner,
        bool requirePin = true)
    {
        if (launchTarget is null)
        {
            throw new ProfileValidationException(
                $"{owner} launch target is missing.");
        }

        ValidateHttpsUrl(launchTarget.EntryUrl, $"{owner} entry URL");
        ValidateText(launchTarget.Label, "launch target label");
        if (requirePin)
        {
            ValidatePinPlacement(
                launchTarget.PinToStart,
                launchTarget.PinToTaskbar,
                owner);
        }
    }

    private static void ValidateHttpsUrl(Uri url, string name)
    {
        if (url is null
            || !url.IsAbsoluteUri
            || url.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(url.Host)
            || !string.IsNullOrEmpty(url.UserInfo)
            || url.AbsoluteUri.Length > MaximumUrlLength)
        {
            throw new ProfileValidationException($"{name} is invalid.");
        }
    }

    private static void ValidateEdgeDestination(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumUrlLength
            || !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || value.Any(char.IsControl))
        {
            throw new ProfileValidationException($"{name} is invalid.");
        }
    }

    private static void ValidateText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumTextLength
            || value.Any(char.IsControl))
        {
            throw new ProfileValidationException($"The {name} is invalid.");
        }
    }

    private static void ValidateIdentifier(string value, string name)
    {
        ValidateText(value, name);
        if (value.Any(
                character => !char.IsAsciiLetterOrDigit(character)
                    && character is not '-' and not '_'))
        {
            throw new ProfileValidationException(
                $"The {name} contains unsupported characters.");
        }
    }

    private static void EnsureUnique(
        IEnumerable<string?> values,
        string errorMessage,
        StringComparer? comparer = null)
    {
        var entries = values.ToArray();
        if (entries.Any(string.IsNullOrWhiteSpace)
            || entries.Distinct(comparer ?? StringComparer.Ordinal).Count()
                != entries.Length)
        {
            throw new ProfileValidationException(errorMessage);
        }
    }

    private static void ValidatePinPlacement(
        bool pinToStart,
        bool pinToTaskbar,
        string owner)
    {
        if (!pinToStart && !pinToTaskbar)
        {
            throw new ProfileValidationException(
                $"Launch target '{owner}' must be pinned to Start or the taskbar.");
        }
    }
}
