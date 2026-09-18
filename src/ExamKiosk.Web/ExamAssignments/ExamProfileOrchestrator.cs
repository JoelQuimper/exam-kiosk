using ExamKiosk.Contracts;
using ExamKiosk.ProfileValidation;
using ExamKiosk.Web.EdgePolicy;
using ExamKiosk.Web.ExamAssignments.Models;

namespace ExamKiosk.Web.ExamAssignments;

public sealed class ExamProfileOrchestrator(IEdgePolicyFactory edgePolicyFactory)
    : IExamProfileOrchestrator
{
    private const int SchemaVersion = 1;

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
                false,
                false));
        var tools = assignment.Tools.ToArray();
        var edgePolicy = edgePolicyFactory.Create(tools);
        var student = new EffectiveStudent(userPrincipalName.Trim());

        var profile = new EffectiveExamProfile(
            SchemaVersion,
            assignment.AssignmentId,
            student,
            exam,
            tools,
            edgePolicy);
        EffectiveExamIntentValidator.Validate(profile);
        return profile;
    }
}
