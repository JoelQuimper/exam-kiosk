using ExamKiosk.Contracts;
using ExamKiosk.Web.ExamAssignments.Models;

namespace ExamKiosk.Web.ExamAssignments;

public sealed class ExamProfileOrchestrator
{
    private const int SchemaVersion = 1;
    private static readonly string[] BaselineAllowedUrls =
    [
        "https://login.microsoftonline.com/",
    ];

    public EffectiveExamProfile Create(
        string userPrincipalName,
        AssignedExam assignment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrincipalName);
        ArgumentNullException.ThrowIfNull(assignment);

        var exam = new EffectiveExam(
            assignment.Exam.ExamId,
            assignment.Exam.Title,
            assignment.Exam.Icon,
            assignment.ExamTarget);
        var tools = assignment.Tools.ToArray();
        var allowedUrls = BaselineAllowedUrls
            .Append(assignment.ExamTarget.AbsoluteUri)
            .Concat(assignment.AllowedUrls)
            .Concat(tools.SelectMany(GetToolAllowedUrls))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var student = new EffectiveStudent(userPrincipalName.Trim());

        return new EffectiveExamProfile(
            SchemaVersion,
            assignment.AssignmentId,
            student,
            exam,
            tools,
            allowedUrls);
    }

    private static IReadOnlyList<string> GetToolAllowedUrls(
        ToolDefinition tool) =>
        tool switch
        {
            DesktopToolDefinition desktop =>
                desktop.Configuration.AllowedUrls,
            WebToolDefinition web =>
                web.Configuration.AllowedUrls,
            _ => throw new InvalidOperationException(
                $"Tool '{tool.ToolId}' has an unsupported definition type."),
        };
}
