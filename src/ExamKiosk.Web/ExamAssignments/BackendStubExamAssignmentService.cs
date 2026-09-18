using System.Collections.Frozen;
using ExamKiosk.Contracts;
using ExamKiosk.Web.ExamAssignments.Models;

namespace ExamKiosk.Web.ExamAssignments;

public sealed class BackendStubExamAssignmentService : IExamAssignmentService
{
    private static readonly FrozenDictionary<string, StudentProfile> StudentsByUpn =
        new[]
        {
            new StudentProfile(
                "student-1",
                "student1@jqdev.onmicrosoft.com",
                "Student 1"),
            new StudentProfile(
                "student-2",
                "student2@jqdev.onmicrosoft.com",
                "Student 2"),
            new StudentProfile(
                "student-3",
                "student3@jqdev.onmicrosoft.com",
                "Student 3"),
            new StudentProfile(
                "student-4",
                "student4@jqdev.onmicrosoft.com",
                "Student 4"),
        }.ToFrozenDictionary(
            student => student.UserPrincipalName,
            StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, ExamDefinition> ExamsById =
        new[]
        {
            new ExamDefinition(
                "exam-1",
                "Mathématiques secondaire 4 — Modélisation financière",
                "calculator"),
            new ExamDefinition(
                "exam-2",
                "Sciences secondaire 4 — Analyse de données",
                "chart"),
        }.ToFrozenDictionary(exam => exam.ExamId, StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, ToolDefinition> ToolsById =
        new ToolDefinition[]
        {
            new DesktopToolDefinition(
                "microsoft-word",
                "Microsoft Word",
                "word",
                true,
                new DesktopToolConfiguration(
                    [
                        new DesktopExecutableDefinition(
                            "word",
                            ApplicationRole.Primary,
                            "%ProgramFiles%\\Microsoft Office\\root\\Office16\\WINWORD.EXE",
                            "Microsoft.Office.WINWORD.EXE.15",
                            new ExecutableValidation(
                                "Microsoft Corporation",
                                null)),
                    ],
                    new DesktopLaunchTarget(
                        "word",
                        "Microsoft Word",
                        true,
                        true))),
            new DesktopToolDefinition(
                "windows-calculator",
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
            new WebToolDefinition(
                "usito-dictionary",
                "Dictionnaire Usito",
                "dictionary",
                true,
                new WebToolConfiguration(
                    ["https://.usito.usherbrooke.ca"],
                    new WebLaunchTarget(
                        new Uri("https://usito.usherbrooke.ca/"),
                        "Dictionnaire Usito",
                        true,
                        true))),
        }.ToFrozenDictionary(tool => tool.ToolId, StringComparer.Ordinal);

    private static readonly IReadOnlyList<StudentExamAssignment> Assignments =
    [
        new(
            "student1-exam1",
            "student-1",
            "exam-1",
            new Uri(
                "https://jqdev.sharepoint.com/sites/ExamSite/Shared%20Documents/Student1-Exam1"),
            ["windows-calculator"]),
        new(
            "student1-exam2",
            "student-1",
            "exam-2",
            new Uri(
                "https://jqdev.sharepoint.com/sites/ExamSite/Shared%20Documents/Student1-Exam2"),
            []),
        new(
            "student2-exam1",
            "student-2",
            "exam-1",
            new Uri(
                "https://jqdev.sharepoint.com/sites/ExamSite/Shared%20Documents/Student2-Exam1"),
            ["microsoft-word"]),
        new(
            "student3-exam2",
            "student-3",
            "exam-2",
            new Uri(
                "https://jqdev.sharepoint.com/sites/ExamSite/Shared%20Documents/Student3-Exam2"),
            ["microsoft-word", "windows-calculator", "usito-dictionary"]),
    ];

    public IReadOnlyList<AssignedExam> GetAssignments(string userPrincipalName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrincipalName);

        if (!StudentsByUpn.TryGetValue(userPrincipalName.Trim(), out var student))
        {
            return [];
        }

        return Assignments
            .Where(assignment => assignment.StudentId == student.StudentId)
            .Select(CreateAssignedExam)
            .ToArray();
    }

    public AssignedExam? GetAssignment(
        string userPrincipalName,
        string assignmentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assignmentId);

        return GetAssignments(userPrincipalName)
            .SingleOrDefault(
                assignment => string.Equals(
                    assignment.AssignmentId,
                    assignmentId,
                    StringComparison.Ordinal));
    }

    private static AssignedExam CreateAssignedExam(StudentExamAssignment assignment)
    {
        if (!ExamsById.TryGetValue(assignment.ExamId, out var exam))
        {
            throw new InvalidOperationException(
                $"Assignment '{assignment.AssignmentId}' references unknown exam '{assignment.ExamId}'.");
        }

        var tools = assignment.ToolIds
            .Select(toolId =>
            {
                if (!ToolsById.TryGetValue(toolId, out var tool))
                {
                    throw new InvalidOperationException(
                        $"Assignment '{assignment.AssignmentId}' references unknown tool '{toolId}'.");
                }

                return tool;
            })
            .ToArray();

        return new AssignedExam(
            assignment.AssignmentId,
            exam,
            assignment.SharePointFolderUrl,
            tools);
    }
}
