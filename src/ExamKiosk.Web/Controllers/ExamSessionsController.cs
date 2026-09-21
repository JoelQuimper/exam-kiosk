using ExamKiosk.Contracts;
using ExamKiosk.Web.Authentication;
using ExamKiosk.Web.ExamSessions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace ExamKiosk.Web.Controllers;

[ApiController]
[Route("api/v1/exam-sessions")]
public sealed class ExamSessionsController(
    IExamSessionStore examSessionStore,
    IAntiforgery antiforgery) : ControllerBase
{
    [HttpPost("active/complete")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<ExamSession>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExamSession>> CompleteActive()
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

        var result = examSessionStore.CompleteActive(userPrincipalName);
        return result.Status switch
        {
            ExamSessionCompletionStatus.Completed => Ok(result.Session),
            ExamSessionCompletionStatus.NotFound => NotFound(),
            _ => throw new InvalidOperationException(
                $"Unknown completion status '{result.Status}'."),
        };
    }

    [HttpPost("{sessionId:guid}/cancel")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<ExamSession>(StatusCodes.Status200OK)]
    [ProducesResponseType<ExamSession>(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExamSession>> Cancel(Guid sessionId)
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

        var result = examSessionStore.Cancel(userPrincipalName, sessionId);
        return result.Status switch
        {
            ExamSessionCancellationStatus.Cancelled => Ok(result.Session),
            ExamSessionCancellationStatus.NotFound => NotFound(),
            ExamSessionCancellationStatus.Conflict => Conflict(result.Session),
            _ => throw new InvalidOperationException(
                $"Unknown cancellation status '{result.Status}'."),
        };
    }
}
