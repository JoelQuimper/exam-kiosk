using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using ExamKiosk.Contracts;
using ExamKiosk.WindowsConfiguration.AssignedAccess;
using ExamKiosk.WindowsConfiguration.Models;

namespace ExamKiosk.WindowsConfiguration.Tests;

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

        Assert.Equal(AssignedAccessProfileConventions.ArtifactFormat, artifact.Format);
        Assert.Equal(
            AssignedAccessProfileConventions.ArtifactSchemaVersion,
            artifact.SchemaVersion);
        Assert.Equal(
            Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(artifact.Xml))),
            artifact.Sha256);
        Assert.Equal(first.ClientVersion, second.ClientVersion);
        Assert.Equal(first.AssignedAccess, second.AssignedAccess);
        Assert.Equal(first.Shortcuts, second.Shortcuts);
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
            pin => pin.TryGetProperty("desktopAppLink", out var value)
                && value.GetString()!.EndsWith(
                    @"\tool-word.lnk",
                    StringComparison.Ordinal));
        Assert.Contains(
            pinnedList,
            pin => pin.TryGetProperty("packagedAppId", out var value)
                && value.GetString()
                    == "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App");

        var taskbarDocument = XDocument.Parse(
            document.Descendants(V5Namespace + "TaskbarLayout").Single().Value);
        var taskbarLinks = taskbarDocument
            .Descendants(TaskbarNamespace + "DesktopApp")
            .Select(element => (string?)element.Attribute("DesktopApplicationLinkPath"))
            .ToArray();
        Assert.DoesNotContain(
            taskbarLinks,
            link => link?.EndsWith(@"\exam.lnk", StringComparison.OrdinalIgnoreCase)
                == true);
        Assert.Contains(
            taskbarLinks,
            link => link?.EndsWith(@"\tool-word.lnk", StringComparison.Ordinal)
                == true);
    }

    [Fact]
    public void Compile_DescribesOnlyToolShortcutArtifacts()
    {
        var shortcuts = compiler
            .Compile(SupportedVersion(), CreateProfile())
            .Shortcuts;

        Assert.DoesNotContain(
            shortcuts,
            shortcut => shortcut.ShortcutId == "exam");
        var word = Assert.IsType<DesktopWindowsShortcutArtifact>(
            shortcuts.Single(shortcut => shortcut.ShortcutId == "tool-word"));
        Assert.Equal("word", word.ApplicationId);
        Assert.DoesNotContain(
            shortcuts,
            shortcut => shortcut.ShortcutId == "tool-calculator");
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

    private static EffectiveExamProfile CreateProfile()
    {
        ToolDefinition[] tools =
        [
            new DesktopToolDefinition(
                "word",
                "Microsoft Word",
                "document",
                true,
                new DesktopToolConfiguration(
                    [
                        new DesktopExecutableDefinition(
                            "word",
                            ApplicationRole.Primary,
                            @"%ProgramFiles%\Microsoft Office\root\Office16\WINWORD.EXE",
                            new ExecutableValidation("Microsoft Corporation", null)),
                    ],
                    new DesktopLaunchTarget("word", "Microsoft Word", true, true))),
            new DesktopToolDefinition(
                "calculator",
                "Calculator",
                "calculator",
                true,
                new DesktopToolConfiguration(
                    [
                        new PackagedApplicationDefinition(
                            "calculator",
                            ApplicationRole.Primary,
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
                new Uri("https://example.com/exam"),
                new WebLaunchTarget(
                    new Uri("https://example.com/exam"),
                    "Open exam",
                    true,
                    true)),
            tools,
            new EffectiveEdgePolicy(["*"], ["https://example.com"]));
    }

    private static WindowsClientVersion SupportedVersion() =>
        new(10, 0, 22621);
}
