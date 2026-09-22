using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using ExamKiosk.Contracts;
using ExamKiosk.DeviceAgent.WindowsConfiguration;
using ExamKiosk.DeviceAgent.WindowsConfiguration.AssignedAccess;
using ExamKiosk.DeviceAgent.WindowsConfiguration.Models;

namespace ExamKiosk.DeviceAgent.Tests.WindowsConfiguration;

public sealed class WindowsConfigurationCompilerTests
{
    private static readonly XNamespace AssignedAccessNamespace =
        AssignedAccessProfileConventions.AssignedAccessNamespace;
    private static readonly XNamespace Rs5Namespace =
        AssignedAccessProfileConventions.Rs5Namespace;
    private static readonly XNamespace V5Namespace =
        "http://schemas.microsoft.com/AssignedAccess/2022/config";
    private static readonly XNamespace TaskbarNamespace =
        "http://schemas.microsoft.com/Start/2014/TaskbarLayout";

    private readonly WindowsConfigurationCompiler compiler = new();

    [Fact]
    public void Compile_GeneratesDeterministicArtifact()
    {
        var profile = CreateProfile();

        var first = compiler.Compile(SupportedVersion(), profile);
        var second = compiler.Compile(SupportedVersion(), profile);
        var artifact = first.AssignedAccess;

        Assert.Equal(
            Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(artifact.Xml))),
            artifact.Sha256);
        Assert.Equal(first.AssignedAccess, second.AssignedAccess);
        Assert.Equal(first.Shortcuts, second.Shortcuts);
        Assert.Equal(
            first.EdgePolicy.UrlBlocklist,
            second.EdgePolicy.UrlBlocklist);
        Assert.Equal(
            first.EdgePolicy.UrlAllowlist,
            second.EdgePolicy.UrlAllowlist);
    }

    [Fact]
    public void Compile_CreatesDenyAllEdgePolicyWithProfileAllowlist()
    {
        var profile = CreateProfile() with
        {
            AllowedUrls =
            [
                "https://example.com/exam",
                "https://cdn.example.com/",
                "https://EXAMPLE.com/exam",
            ],
        };

        var policy = compiler
            .Compile(SupportedVersion(), profile)
            .EdgePolicy;

        Assert.Equal(["*"], policy.UrlBlocklist);
        Assert.Equal(
            [
                "https://example.com/exam",
                "https://cdn.example.com/",
            ],
            policy.UrlAllowlist);
    }

    [Theory]
    [InlineData("")]
    [InlineData("example.com")]
    [InlineData("file:///C:/exam.txt")]
    public void Compile_WhenAllowedUrlIsInvalid_Throws(string allowedUrl)
    {
        var profile = CreateProfile() with
        {
            AllowedUrls = [allowedUrl],
        };

        var exception = Assert.Throws<WindowsConfigurationException>(
            () => compiler.Compile(SupportedVersion(), profile));

        Assert.Contains("Allowed URL", exception.Message);
    }

    [Fact]
    public void Compile_IncludesBaselineAndAssignedApplications()
    {
        var configuration = compiler.Compile(SupportedVersion(), CreateProfile());
        var document = XDocument.Parse(configuration.AssignedAccess.Xml);
        var apps = document
            .Descendants(AssignedAccessNamespace + "App")
            .ToArray();

        Assert.Contains(
            apps,
            app => (string?)app.Attribute("DesktopAppPath")
                == AssignedAccessProfileConventions.RestrictedClientPath);
        Assert.Contains(
            apps,
            app => (string?)app.Attribute("DesktopAppPath")
                == @"%ProgramFiles%\Microsoft Office\root\Office16\WINWORD.EXE");
        Assert.Contains(
            apps,
            app => (string?)app.Attribute("AppUserModelId")
                == "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App");
        Assert.Single(
            apps,
            app => (string?)app.Attribute(Rs5Namespace + "AutoLaunch")
                == "true");

        var assignedProfile = document
            .Descendants(AssignedAccessNamespace + "Profile")
            .Single();
        Assert.Equal(
            "EXAM — student — exam-1",
            (string?)assignedProfile.Attribute("Name"));
    }

    [Fact]
    public void Compile_PinsRequestedToolsButNotExam()
    {
        var configuration = compiler.Compile(SupportedVersion(), CreateProfile());
        var document = XDocument.Parse(configuration.AssignedAccess.Xml);
        var startPins = document
            .Descendants(V5Namespace + "StartPins")
            .Single()
            .Value;
        using var startDocument = JsonDocument.Parse(startPins);
        var pinnedList = startDocument.RootElement
            .GetProperty("pinnedList")
            .EnumerateArray()
            .ToArray();

        Assert.DoesNotContain(
            pinnedList,
            pin => pin.TryGetProperty("desktopAppLink", out var value)
                && value.GetString()!.EndsWith(
                    @"\exam.lnk",
                    StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            pinnedList,
            pin => pin.TryGetProperty("desktopAppId", out var value)
                && value.GetString() == "Microsoft.Office.WINWORD.EXE.15");
        Assert.Contains(
            pinnedList,
            pin => pin.TryGetProperty("packagedAppId", out var value)
                && value.GetString()
                    == "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App");

        var taskbarDocument = XDocument.Parse(
            document.Descendants(V5Namespace + "TaskbarLayout").Single().Value);
        var taskbarDesktopApps = taskbarDocument
            .Descendants(TaskbarNamespace + "DesktopApp")
            .ToArray();
        Assert.DoesNotContain(
            taskbarDesktopApps,
            element => ((string?)element.Attribute("DesktopApplicationLinkPath"))
                    ?.EndsWith(@"\exam.lnk", StringComparison.OrdinalIgnoreCase)
                == true);
        Assert.Contains(
            taskbarDesktopApps,
            element => (string?)element.Attribute("DesktopApplicationID")
                == "Microsoft.Office.WINWORD.EXE.15");
    }

    [Fact]
    public void Compile_WithDirectLaunchTargets_DoesNotCreateShortcuts()
    {
        var shortcuts = compiler
            .Compile(SupportedVersion(), CreateProfile())
            .Shortcuts;

        Assert.Empty(shortcuts);
    }

    [Fact]
    public void Compile_WhenDesktopApplicationIdIsMissing_UsesShortcut()
    {
        var configuration = compiler.Compile(
            SupportedVersion(),
            CreateProfile(wordDesktopApplicationId: null));
        var document = XDocument.Parse(configuration.AssignedAccess.Xml);
        var startPins = document
            .Descendants(V5Namespace + "StartPins")
            .Single()
            .Value;
        using var startDocument = JsonDocument.Parse(startPins);

        Assert.Contains(
            startDocument.RootElement
                .GetProperty("pinnedList")
                .EnumerateArray(),
            pin => pin.TryGetProperty("desktopAppLink", out var value)
                && value.GetString()!.EndsWith(
                    @"\Microsoft Word.lnk",
                    StringComparison.Ordinal));

        var taskbarDocument = XDocument.Parse(
            document.Descendants(V5Namespace + "TaskbarLayout").Single().Value);
        Assert.Contains(
            taskbarDocument.Descendants(TaskbarNamespace + "DesktopApp"),
            element => ((string?)element.Attribute("DesktopApplicationLinkPath"))
                    ?.EndsWith(@"\Microsoft Word.lnk", StringComparison.Ordinal)
                    == true);

        Assert.IsType<DesktopWindowsShortcutArtifact>(
            configuration.Shortcuts.Single(
                shortcut => shortcut.ShortcutId == "tool-word"));
    }

    [Fact]
    public void Compile_WhenWindowsBuildIsUnsupported_Throws()
    {
        var exception = Assert.Throws<NotSupportedException>(
            () => compiler.Compile(
                new WindowsClientVersion(10, 0, 19045),
                CreateProfile()));

        Assert.Contains("10.0.19045", exception.Message);
    }

    private static EffectiveExamProfile CreateProfile(
        string? wordDesktopApplicationId =
            "Microsoft.Office.WINWORD.EXE.15")
    {
        ToolDefinition[] tools =
        [
            new DesktopToolDefinition(
                "word",
                "Microsoft Word",
                "document",
                new DesktopToolConfiguration(
                    [
                        new DesktopExecutableDefinition(
                            "word",
                            @"%ProgramFiles%\Microsoft Office\root\Office16\WINWORD.EXE",
                            wordDesktopApplicationId),
                    ],
                    new DesktopLaunchTarget("word", "Microsoft Word", true, true))),
            new DesktopToolDefinition(
                "calculator",
                "Calculator",
                "calculator",
                new DesktopToolConfiguration(
                    [
                        new PackagedApplicationDefinition(
                            "calculator",
                            "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"),
                    ],
                    new DesktopLaunchTarget(
                        "calculator",
                        "Calculator",
                        true,
                        true))),
        ];

        return new EffectiveExamProfile(
            1,
            "assignment-1",
            new EffectiveStudent("student@example.com"),
            new EffectiveExam(
                "exam-1",
                "Mathematics",
                "calculator",
                new Uri("https://example.com/exam")),
            tools,
            ["https://example.com"]);
    }

    private static WindowsClientVersion SupportedVersion() =>
        new(10, 0, 22621);
}
