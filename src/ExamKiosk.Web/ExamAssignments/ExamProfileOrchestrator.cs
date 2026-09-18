using ExamKiosk.Contracts;
using ExamKiosk.Web.AssignedAccess;
using ExamKiosk.Web.EdgePolicy;
using ExamKiosk.Web.ExamAssignments.Models;

namespace ExamKiosk.Web.ExamAssignments;

public sealed class ExamProfileOrchestrator(
    IEdgePolicyFactory edgePolicyFactory,
    IAssignedAccessFactory assignedAccessFactory)
    : IExamProfileOrchestrator
{
    private const int SchemaVersion = 1;
    private static readonly WindowsClientVersion PreviewClientWindowsVersion =
        new(10, 0, 22621);

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
            assignment.SharePointFolderUrl,
            new WebLaunchTarget(
                assignment.SharePointFolderUrl,
                "Open exam",
                true,
                true));
        var tools = assignment.Tools.ToArray();
        var edgePolicy = edgePolicyFactory.Create(tools);
        var student = new EffectiveStudent(userPrincipalName.Trim());
        var windowsConfiguration = assignedAccessFactory.Create(
            PreviewClientWindowsVersion,
            student,
            exam,
            tools);

        return new EffectiveExamProfile(
            SchemaVersion,
            assignment.AssignmentId,
            student,
            exam,
            tools,
            edgePolicy,
            windowsConfiguration);
    }
}
