using ExamKiosk.Web.Authentication;
using ExamKiosk.Web.ExamAssignments;
using Microsoft.AspNetCore.Mvc;

namespace ExamKiosk.Web.Controllers;

[ApiController]
[Route("api/v1/exam-assignments")]
public sealed class ExamAssignmentsController(
    IExamAssignmentService examAssignmentService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ExamAssignmentResponse>>(
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<IReadOnlyList<ExamAssignmentResponse>> Get()
    {
        var userPrincipalName = AuthenticatedUpnResolver.Resolve(User);
        if (userPrincipalName is null)
        {
            return Forbid();
        }

        var response = examAssignmentService
            .GetAssignments(userPrincipalName)
            .Select(ExamAssignmentResponse.FromAssignedExam)
            .ToArray();

        return Ok(response);
    }
}

public sealed record ExamAssignmentResponse(
    string AssignmentId,
    ExamResponse Exam,
    IReadOnlyList<ToolResponse> Tools)
{
    internal static ExamAssignmentResponse FromAssignedExam(AssignedExam assignment) =>
        new(
            assignment.AssignmentId,
            new ExamResponse(
                assignment.Exam.ExamId,
                assignment.Exam.Title,
                assignment.Exam.Icon),
            assignment.Tools
                .Select(tool => new ToolResponse(
                    tool.ToolId,
                    tool.DisplayName,
                    tool.Icon))
                .ToArray());
}

public sealed record ExamResponse(
    string Id,
    string Title,
    string Icon);

public sealed record ToolResponse(
    string Id,
    string DisplayName,
    string Icon);
