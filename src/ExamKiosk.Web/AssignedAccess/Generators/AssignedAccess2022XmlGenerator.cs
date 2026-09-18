using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using ExamKiosk.Contracts;
using ExamKiosk.Web.AssignedAccess.Generators.Models;

namespace ExamKiosk.Web.AssignedAccess.Generators;

public sealed class AssignedAccess2022XmlGenerator : IAssignedAccessXmlGenerator
{
    private const int WindowsNtMajorVersion = 10;
    private const int Windows11Version22H2Build = 22621;
    private const string ProfileId = "{9A2A490F-10F6-4764-974A-43B19E722C23}";
    private const string ShortcutRoot =
        @"%ALLUSERSPROFILE%\Microsoft\Windows\Start Menu\Programs\Exam Kiosk";

    private static readonly XNamespace AssignedAccessNamespace =
        "http://schemas.microsoft.com/AssignedAccess/2017/config";
    private static readonly XNamespace Rs5Namespace =
        "http://schemas.microsoft.com/AssignedAccess/201810/config";
    private static readonly XNamespace V5Namespace =
        "http://schemas.microsoft.com/AssignedAccess/2022/config";
    private static readonly XNamespace LayoutNamespace =
        "http://schemas.microsoft.com/Start/2014/LayoutModification";
    private static readonly XNamespace DefaultLayoutNamespace =
        "http://schemas.microsoft.com/Start/2014/FullDefaultLayout";
    private static readonly XNamespace StartNamespace =
        "http://schemas.microsoft.com/Start/2014/StartLayout";
    private static readonly XNamespace TaskbarNamespace =
        "http://schemas.microsoft.com/Start/2014/TaskbarLayout";

    private static readonly JsonSerializerOptions StartPinsSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public bool Supports(WindowsClientVersion clientVersion)
    {
        ArgumentNullException.ThrowIfNull(clientVersion);

        return clientVersion.Major == WindowsNtMajorVersion
            && clientVersion.Minor == 0
            && clientVersion.Build >= Windows11Version22H2Build;
    }

    public EffectiveWindowsConfiguration Generate(
        WindowsClientVersion clientVersion,
        EffectiveStudent student,
        EffectiveExam exam,
        IReadOnlyList<ToolDefinition> tools)
    {
        ArgumentNullException.ThrowIfNull(exam);
        ArgumentNullException.ThrowIfNull(student);
        ArgumentNullException.ThrowIfNull(tools);
        if (!Supports(clientVersion))
        {
            throw new ArgumentException(
                "The client version is not supported by the 2022 Assigned Access schema.",
                nameof(clientVersion));
        }

        var shortcuts = CreateShortcuts(tools);
        var allowedApps = CreateAllowedApps(tools);
        var startPins = CreateStartPins(tools, shortcuts);
        var taskbarLayout = CreateTaskbarLayout(tools, shortcuts);
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                AssignedAccessNamespace + "AssignedAccessConfiguration",
                new XAttribute(XNamespace.Xmlns + "rs5", Rs5Namespace),
                new XAttribute(XNamespace.Xmlns + "v5", V5Namespace),
                new XElement(
                    AssignedAccessNamespace + "Profiles",
                    new XElement(
                        AssignedAccessNamespace + "Profile",
                        new XAttribute("Id", ProfileId),
                        new XAttribute(
                            "Name",
                            CreateProfileName(student, exam)),
                        new XElement(
                            AssignedAccessNamespace + "AllAppsList",
                            new XElement(
                                AssignedAccessNamespace + "AllowedApps",
                                allowedApps)),
                        new XElement(
                            V5Namespace + "StartPins",
                            new XCData(startPins)),
                        new XElement(
                            AssignedAccessNamespace + "Taskbar",
                            new XAttribute("ShowTaskbar", "true")),
                        new XElement(
                            V5Namespace + "TaskbarLayout",
                            new XCData(taskbarLayout)))),
                new XElement(
                    AssignedAccessNamespace + "Configs",
                    new XElement(
                        AssignedAccessNamespace + "Config",
                        new XElement(
                            AssignedAccessNamespace + "AutoLogonAccount",
                            new XAttribute(
                                Rs5Namespace + "DisplayName",
                                "Exam Kiosk")),
                        new XElement(
                            AssignedAccessNamespace + "DefaultProfile",
                            new XAttribute("Id", ProfileId))))));

        var xmlBytes = SerializeXml(document);
        var xml = Encoding.UTF8.GetString(xmlBytes);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(xmlBytes));

        return new EffectiveWindowsConfiguration(
            clientVersion,
            new AssignedAccessArtifact(
                "windowsAssignedAccessXml",
                "2022",
                new AssignedAccessSource("generated", 1),
                "utf-8",
                sha256,
                xml),
            shortcuts);
    }

    private static string CreateProfileName(
        EffectiveStudent student,
        EffectiveExam exam)
    {
        var separatorIndex = student.UserPrincipalName.IndexOf('@');
        if (separatorIndex <= 0)
        {
            throw new InvalidOperationException(
                "The student user principal name does not contain an alias.");
        }

        return $"EXAM — {student.UserPrincipalName[..separatorIndex]} — {exam.Id}";
    }

    private static XElement[] CreateAllowedApps(
        IReadOnlyList<ToolDefinition> tools)
    {
        var applications = new List<XElement>
        {
            DesktopApp(
                @"%ProgramFiles%\ExamKiosk\RestrictedClient\ExamKiosk.RestrictedClient.exe",
                autoLaunch: true),
            DesktopApp(
                @"%ProgramFiles(x86)%\Microsoft\EdgeWebView\Application\*\msedgewebview2.exe"),
            DesktopApp(
                @"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe"),
            DesktopApp(
                @"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge_proxy.exe"),
            PackagedApp("Microsoft.MicrosoftEdge.Stable_8wekyb3d8bbwe!App"),
        };

        foreach (var application in tools
                     .OfType<DesktopToolDefinition>()
                     .SelectMany(tool => tool.Configuration.Applications))
        {
            applications.Add(
                application switch
                {
                    DesktopExecutableDefinition executable =>
                        DesktopApp(executable.Path),
                    PackagedApplicationDefinition packaged =>
                        PackagedApp(packaged.AppUserModelId),
                    _ => throw new InvalidOperationException(
                        $"Application '{application.ApplicationId}' has an unsupported type."),
                });
        }

        return applications
            .GroupBy(element => element.ToString(SaveOptions.DisableFormatting))
            .Select(group => group.First())
            .ToArray();
    }

    private static IReadOnlyList<WindowsShortcutArtifact> CreateShortcuts(
        IReadOnlyList<ToolDefinition> tools)
    {
        var shortcuts = new List<WindowsShortcutArtifact>();
        foreach (var tool in tools)
        {
            WindowsShortcutArtifact? shortcut = tool switch
            {
                DesktopToolDefinition desktop
                    when (desktop.Configuration.LaunchTarget.PinToStart
                            || desktop.Configuration.LaunchTarget.PinToTaskbar)
                        && desktop.Configuration.Applications
                            .Single(application =>
                                application.ApplicationId
                                == desktop.Configuration.LaunchTarget.ApplicationId)
                            is DesktopExecutableDefinition =>
                    new DesktopWindowsShortcutArtifact(
                        $"tool-{tool.ToolId}",
                        LinkPath($"tool-{tool.ToolId}"),
                        desktop.Configuration.LaunchTarget.Label,
                        desktop.Configuration.LaunchTarget.ApplicationId),
                WebToolDefinition web
                    when web.Configuration.LaunchTarget.PinToStart
                        || web.Configuration.LaunchTarget.PinToTaskbar =>
                    new WebWindowsShortcutArtifact(
                        $"tool-{tool.ToolId}",
                        LinkPath($"tool-{tool.ToolId}"),
                        web.Configuration.LaunchTarget.Label,
                        web.Configuration.LaunchTarget.EntryUrl),
                _ => null,
            };

            if (shortcut is not null)
            {
                shortcuts.Add(shortcut);
            }
        }

        return shortcuts;
    }

    private static string CreateStartPins(
        IReadOnlyList<ToolDefinition> tools,
        IReadOnlyList<WindowsShortcutArtifact> shortcuts)
    {
        var pins = new List<StartPin>();
        foreach (var tool in tools)
        {
            switch (tool)
            {
                case DesktopToolDefinition desktop
                    when desktop.Configuration.LaunchTarget.PinToStart:
                    var application = desktop.Configuration.Applications.Single(
                        candidate =>
                            candidate.ApplicationId
                            == desktop.Configuration.LaunchTarget.ApplicationId);
                    pins.Add(
                        application is PackagedApplicationDefinition packaged
                            ? new StartPin(null, packaged.AppUserModelId)
                            : new StartPin(
                                FindShortcut(
                                    shortcuts,
                                    $"tool-{tool.ToolId}").LinkPath,
                                null));
                    break;
                case WebToolDefinition web
                    when web.Configuration.LaunchTarget.PinToStart:
                    pins.Add(
                        new StartPin(
                            FindShortcut(
                                shortcuts,
                                $"tool-{tool.ToolId}").LinkPath,
                            null));
                    break;
            }
        }

        return JsonSerializer.Serialize(
            new StartPinLayout(pins),
            StartPinsSerializerOptions);
    }

    private static string CreateTaskbarLayout(
        IReadOnlyList<ToolDefinition> tools,
        IReadOnlyList<WindowsShortcutArtifact> shortcuts)
    {
        var pinList = new XElement(TaskbarNamespace + "TaskbarPinList");
        foreach (var tool in tools)
        {
            switch (tool)
            {
                case DesktopToolDefinition desktop
                    when desktop.Configuration.LaunchTarget.PinToTaskbar:
                    var application = desktop.Configuration.Applications.Single(
                        candidate =>
                            candidate.ApplicationId
                            == desktop.Configuration.LaunchTarget.ApplicationId);
                    pinList.Add(
                        application is PackagedApplicationDefinition packaged
                            ? new XElement(
                                TaskbarNamespace + "UWA",
                                new XAttribute(
                                    "AppUserModelID",
                                    packaged.AppUserModelId))
                            : TaskbarDesktopPin(
                                FindShortcut(
                                    shortcuts,
                                    $"tool-{tool.ToolId}").LinkPath));
                    break;
                case WebToolDefinition web
                    when web.Configuration.LaunchTarget.PinToTaskbar:
                    pinList.Add(
                        TaskbarDesktopPin(
                            FindShortcut(
                                shortcuts,
                                $"tool-{tool.ToolId}").LinkPath));
                    break;
            }
        }

        return Encoding.UTF8.GetString(
            SerializeXml(
                new XDocument(
                    new XDeclaration("1.0", "utf-8", null),
                    new XElement(
                        LayoutNamespace + "LayoutModificationTemplate",
                        new XAttribute(
                            XNamespace.Xmlns + "defaultlayout",
                            DefaultLayoutNamespace),
                        new XAttribute(XNamespace.Xmlns + "start", StartNamespace),
                        new XAttribute(XNamespace.Xmlns + "taskbar", TaskbarNamespace),
                        new XAttribute("Version", "1"),
                        new XElement(
                            LayoutNamespace + "CustomTaskbarLayoutCollection",
                            new XAttribute("PinListPlacement", "Replace"),
                            new XElement(
                                DefaultLayoutNamespace + "TaskbarLayout",
                                pinList))))));
    }

    private static XElement DesktopApp(string path, bool autoLaunch = false)
    {
        var element = new XElement(
            AssignedAccessNamespace + "App",
            new XAttribute("DesktopAppPath", path));
        if (autoLaunch)
        {
            element.Add(new XAttribute(Rs5Namespace + "AutoLaunch", "true"));
        }

        return element;
    }

    private static XElement PackagedApp(string appUserModelId) =>
        new(
            AssignedAccessNamespace + "App",
            new XAttribute("AppUserModelId", appUserModelId));

    private static XElement TaskbarDesktopPin(string linkPath) =>
        new(
            TaskbarNamespace + "DesktopApp",
            new XAttribute("DesktopApplicationLinkPath", linkPath));

    private static WindowsShortcutArtifact FindShortcut(
        IReadOnlyList<WindowsShortcutArtifact> shortcuts,
        string shortcutId) =>
        shortcuts.Single(shortcut => shortcut.ShortcutId == shortcutId);

    private static string LinkPath(string shortcutId) =>
        $@"{ShortcutRoot}\{shortcutId}.lnk";

    private static byte[] SerializeXml(XDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(
                   stream,
                   new XmlWriterSettings
                   {
                       Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                       Indent = true,
                       NewLineChars = "\n",
                       NewLineHandling = NewLineHandling.Replace,
                       OmitXmlDeclaration = false,
                   }))
        {
            document.Save(writer);
        }

        return stream.ToArray();
    }

}
