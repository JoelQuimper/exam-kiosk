using ExamKiosk.Contracts;
using ExamKiosk.Web.Authentication;
using ExamKiosk.Web.Controllers.Models;
using ExamKiosk.Web.ExamAssignments;
using ExamKiosk.Web.ExamSessions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace ExamKiosk.Web.Controllers;

[ApiController]
[Route("api/v1/exam-assignments")]
public sealed class ExamAssignmentsController(
    IExamAssignmentService examAssignmentService,
    IExamProfileOrchestrator examProfileOrchestrator,
    IExamSessionStore examSessionStore,
    IAntiforgery antiforgery) : ControllerBase
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

    [HttpGet("{assignmentId}/profile")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<EffectiveExamProfile>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<EffectiveExamProfile> GetProfile(string assignmentId)
    {
        var userPrincipalName = AuthenticatedUpnResolver.Resolve(User);
        if (userPrincipalName is null)
        {
            return Forbid();
        }

        var assignment = examAssignmentService.GetAssignment(
            userPrincipalName,
            assignmentId);
        if (assignment is null)
        {
            return NotFound();
        }

        return Ok(examProfileOrchestrator.Create(userPrincipalName, assignment));
    }

    [HttpPost("{assignmentId}/sessions")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<ExamSession>(StatusCodes.Status201Created)]
    [ProducesResponseType<ExamSession>(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExamSession>> StartSession(
        string assignmentId)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return BadRequest();
        }

        var userPrincipalName = AuthenticatedUpnResolver.Resolve(User);
        if (userPrincipalName is null)
        {
            return Forbid();
        }

        var assignment = examAssignmentService.GetAssignment(
            userPrincipalName,
            assignmentId);
        if (assignment is null)
        {
            return NotFound();
        }

        var profile = examProfileOrchestrator.Create(
            userPrincipalName,
            assignment);
        var result = examSessionStore.Start(userPrincipalName, profile);
        return result.Created
            ? StatusCode(StatusCodes.Status201Created, result.Session)
            : Conflict(result.Session);
    }
}
