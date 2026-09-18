using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using ExamKiosk.Contracts;
using ExamKiosk.Web.AssignedAccess;
using ExamKiosk.Web.AssignedAccess.Generators;
using ExamKiosk.Web.AssignedAccess.Validation;
using ExamKiosk.Web.EdgePolicy;
using ExamKiosk.Web.EdgePolicy.Validation;
using ExamKiosk.Web.ExamAssignments;
using ExamKiosk.Web.ExamAssignments.Models;

namespace ExamKiosk.Web.Tests;

public sealed class AssignedAccessXmlGeneratorTests
{
    private static readonly XNamespace AssignedAccessNamespace =
        "http://schemas.microsoft.com/AssignedAccess/2017/config";
    private static readonly XNamespace Rs5Namespace =
        "http://schemas.microsoft.com/AssignedAccess/201810/config";
    private static readonly XNamespace V5Namespace =
        "http://schemas.microsoft.com/AssignedAccess/2022/config";
    private static readonly XNamespace TaskbarNamespace =
        "http://schemas.microsoft.com/Start/2014/TaskbarLayout";

    private readonly ExamProfileOrchestrator orchestrator =
        new(
            new EdgePolicyFactory(new EdgePolicyConfigurationValidator()),
            CreateAssignedAccessFactory());
    private readonly BackendStubExamAssignmentService assignments = new();

    [Fact]
    public void Create_GeneratesDeterministicAssignedAccessArtifact()
    {
        var assignment = assignments.GetAssignment(
            "student3@jqdev.onmicrosoft.com",
            "student3-exam2");

        var first = orchestrator.Create(
            "student3@jqdev.onmicrosoft.com",
            assignment!);
        var second = orchestrator.Create(
            "student3@jqdev.onmicrosoft.com",
            assignment!);
        var artifact = first.WindowsConfiguration.AssignedAccess;

        Assert.Equal("windowsAssignedAccessXml", artifact.Format);
        Assert.Equal("2022", artifact.SchemaVersion);
        Assert.Equal("generated", artifact.Source.Type);
        Assert.Equal(1, artifact.Source.GeneratorVersion);
        Assert.Equal("utf-8", artifact.ContentEncoding);
        Assert.Equal(
            new WindowsClientVersion(10, 0, 22621),
            first.WindowsConfiguration.ClientVersion);
        Assert.Equal(
            Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(artifact.Xml))),
            artifact.Sha256);
        Assert.StartsWith(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n",
            artifact.Xml,
            StringComparison.Ordinal);
        Assert.Equal(
            first.WindowsConfiguration.AssignedAccess,
            second.WindowsConfiguration.AssignedAccess);
        Assert.Equal(
            first.WindowsConfiguration.Shortcuts,
            second.WindowsConfiguration.Shortcuts);
    }

    [Fact]
    public void Create_IncludesBaselineAndAssignedApplications()
    {
        var profile = CreateStudent3Profile();
        var document = XDocument.Parse(profile.WindowsConfiguration.AssignedAccess.Xml);
        var apps = document
            .Descendants(AssignedAccessNamespace + "App")
            .ToArray();

        Assert.Contains(
            apps,
            app => (string?)app.Attribute("DesktopAppPath")
                == @"%ProgramFiles%\ExamKiosk\RestrictedClient\ExamKiosk.RestrictedClient.exe");
        Assert.Contains(
            apps,
            app => (string?)app.Attribute("DesktopAppPath")
                == @"%ProgramFiles%\Microsoft Office\root\Office16\WINWORD.EXE");
        Assert.Contains(
            apps,
            app => (string?)app.Attribute("AppUserModelId")
                == "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App");
        Assert.Contains(
            apps,
            app => (string?)app.Attribute("DesktopAppPath")
                == @"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge_proxy.exe");
        Assert.Single(
            apps,
            app => (string?)app.Attribute(Rs5Namespace + "AutoLaunch")
                == "true");

        var profileElement = document
            .Descendants(AssignedAccessNamespace + "Profile")
            .Single();
        var defaultProfile = document
            .Descendants(AssignedAccessNamespace + "DefaultProfile")
            .Single();
        Assert.Equal(
            "{9A2A490F-10F6-4764-974A-43B19E722C23}",
            (string?)profileElement.Attribute("Id"));
        Assert.Equal(
            (string?)profileElement.Attribute("Id"),
            (string?)defaultProfile.Attribute("Id"));
    }

    [Fact]
    public void Create_UsesStartAndTaskbarPinsForRequestedToolsOnly()
    {
        var profile = CreateStudent3Profile();
        var document = XDocument.Parse(profile.WindowsConfiguration.AssignedAccess.Xml);
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
                && value.GetString()
                    == @"%ALLUSERSPROFILE%\Microsoft\Windows\Start Menu\Programs\Exam Kiosk\exam.lnk");
        Assert.Contains(
            pinnedList,
            pin => pin.TryGetProperty("desktopAppLink", out var value)
                && value.GetString()
                    == @"%ALLUSERSPROFILE%\Microsoft\Windows\Start Menu\Programs\Exam Kiosk\tool-microsoft-word.lnk");
        Assert.Contains(
            pinnedList,
            pin => pin.TryGetProperty("packagedAppId", out var value)
                && value.GetString()
                    == "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App");
        Assert.Contains(
            pinnedList,
            pin => pin.TryGetProperty("desktopAppLink", out var value)
                && value.GetString()
                    == @"%ALLUSERSPROFILE%\Microsoft\Windows\Start Menu\Programs\Exam Kiosk\tool-usito-dictionary.lnk");

        var taskbarXml = document
            .Descendants(V5Namespace + "TaskbarLayout")
            .Single()
            .Value;
        var taskbarDocument = XDocument.Parse(taskbarXml);
        var taskbarLinks = taskbarDocument
            .Descendants(TaskbarNamespace + "DesktopApp")
            .Select(element => (string?)element.Attribute("DesktopApplicationLinkPath"))
            .ToArray();
        Assert.DoesNotContain(
            @"%ALLUSERSPROFILE%\Microsoft\Windows\Start Menu\Programs\Exam Kiosk\exam.lnk",
            taskbarLinks);
        Assert.Contains(
            @"%ALLUSERSPROFILE%\Microsoft\Windows\Start Menu\Programs\Exam Kiosk\tool-microsoft-word.lnk",
            taskbarLinks);
        Assert.Contains(
            @"%ALLUSERSPROFILE%\Microsoft\Windows\Start Menu\Programs\Exam Kiosk\tool-usito-dictionary.lnk",
            taskbarLinks);
        Assert.Contains(
            taskbarDocument.Descendants(TaskbarNamespace + "UWA"),
            element => (string?)element.Attribute("AppUserModelID")
                == "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App");
    }

    [Fact]
    public void Create_DescribesValidatedShortcutArtifacts()
    {
        var shortcuts = CreateStudent3Profile().WindowsConfiguration.Shortcuts;

        Assert.DoesNotContain(
            shortcuts,
            shortcut => shortcut.ShortcutId == "exam");

        var word = Assert.IsType<DesktopWindowsShortcutArtifact>(
            shortcuts.Single(
                shortcut => shortcut.ShortcutId == "tool-microsoft-word"));
        Assert.Equal("word", word.ApplicationId);

        var usito = Assert.IsType<WebWindowsShortcutArtifact>(
            shortcuts.Single(
                shortcut => shortcut.ShortcutId == "tool-usito-dictionary"));
        Assert.Equal("https://usito.usherbrooke.ca/", usito.EntryUrl.AbsoluteUri);

        Assert.DoesNotContain(
            shortcuts,
            shortcut => shortcut.ShortcutId == "tool-windows-calculator");
    }

    [Fact]
    public void Create_EscapesProfileTextAndDoesNotEmitDocumentType()
    {
        var assignment = new AssignedExam(
            "escaped-exam",
            new ExamDefinition("exam-escaped", "Science & <Math> \"Exam\"", "chart"),
            new Uri(
                "https://jqdev.sharepoint.com/sites/ExamSite/Shared%20Documents/Escaped"),
            []);

        var artifact = orchestrator
            .Create("student1@jqdev.onmicrosoft.com", assignment)
            .WindowsConfiguration
            .AssignedAccess;
        var document = XDocument.Parse(artifact.Xml);

        Assert.Null(document.DocumentType);
        Assert.Equal(
            assignment.Exam.Title,
            (string?)document
                .Descendants(AssignedAccessNamespace + "Profile")
                .Single()
                .Attribute("Name"));
    }

    [Fact]
    public void Generate_WhenWindowsBuildIsNotSupported_Throws()
    {
        var assignedAccessFactory = CreateAssignedAccessFactory();
        var profile = CreateStudent3Profile();

        var exception = Assert.Throws<NotSupportedException>(
                () => assignedAccessFactory.Create(
                    new WindowsClientVersion(10, 0, 19045),
                    profile.Exam,
                    profile.Tools));

        Assert.Contains("10.0.19045", exception.Message);
    }

    private static AssignedAccessFactory CreateAssignedAccessFactory() =>
        new(
            new AssignedAccessConfigurationValidator(),
            [new AssignedAccess2022XmlGenerator()]);

    private EffectiveExamProfile CreateStudent3Profile()
    {
        var assignment = assignments.GetAssignment(
            "student3@jqdev.onmicrosoft.com",
            "student3-exam2");

        return orchestrator.Create(
            "student3@jqdev.onmicrosoft.com",
            assignment!);
    }
}
