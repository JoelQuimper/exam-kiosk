using ExamKiosk.Contracts;
using ExamKiosk.Web.AssignedAccess;
using ExamKiosk.Web.AssignedAccess.Generators;
using ExamKiosk.Web.AssignedAccess.Validation;
using ExamKiosk.Web.EdgePolicy;
using ExamKiosk.Web.EdgePolicy.Validation;
using ExamKiosk.Web.ExamAssignments;
using ExamKiosk.Web.ExamAssignments.Models;

namespace ExamKiosk.Web.Tests;

public sealed class ExamProfileOrchestratorTests
{
    private readonly ExamProfileOrchestrator orchestrator =
        new(
            new EdgePolicyFactory(new EdgePolicyConfigurationValidator()),
            new AssignedAccessFactory(
                new AssignedAccessConfigurationValidator(),
                [new AssignedAccess2022XmlGenerator()]));

    [Fact]
    public void Create_WhenDesktopShortcutDoesNotReferencePrimaryApplication_Throws()
    {
        var tool = new DesktopToolDefinition(
            "editor",
            "Editor",
            "editor",
            true,
            new DesktopToolConfiguration(
                [
                    new DesktopExecutableDefinition(
                        "editor",
                        ApplicationRole.Helper,
                        "%ProgramFiles%\\Editor\\Editor.exe",
                        new ExecutableValidation("Publisher", null)),
                ],
                new DesktopLaunchTarget("editor", "Editor", true, true)));
        var assignment = ValidAssignment() with { Tools = [tool] };

        var exception = Assert.Throws<InvalidOperationException>(
            () => orchestrator.Create(
                "student1@jqdev.onmicrosoft.com",
                assignment));

        Assert.Contains("primary application", exception.Message);
    }

    [Fact]
    public void Create_WhenToolIdCannotFormSafeShortcutPath_Throws()
    {
        var tool = new WebToolDefinition(
            "../dictionary",
            "Dictionary",
            "dictionary",
            true,
            new WebToolConfiguration(
                ["https://.dictionary.example"],
                new WebLaunchTarget(
                    new Uri("https://dictionary.example/"),
                    "Dictionary",
                    true,
                    true)));
        var assignment = ValidAssignment() with { Tools = [tool] };

        var exception = Assert.Throws<InvalidOperationException>(
            () => orchestrator.Create(
                "student1@jqdev.onmicrosoft.com",
                assignment));

        Assert.Contains("unsupported characters", exception.Message);
    }

    private static AssignedExam ValidAssignment() =>
        new(
            "student1-exam1",
            new ExamDefinition("exam-1", "Exam 1", "calculator"),
            new Uri(
                "https://jqdev.sharepoint.com/sites/ExamSite/Shared%20Documents/Student1-Exam1"),
            []);
}
