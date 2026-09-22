using ExamKiosk.Web.Authentication;
using ExamKiosk.Web.Controllers.Models;
using ExamKiosk.Web.ExamSessions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamKiosk.Web.Controllers;

[ApiController]
[Authorize(
    AuthenticationSchemes = AgentAuthorization.AuthenticationScheme,
    Roles = AgentAuthorization.AppRole)]
[Route("api/v1/device-exam-sessions")]
public sealed class DeviceExamSessionsController(
    IExamSessionStore examSessionStore) : ControllerBase
{
    [HttpPost("{sessionId:guid}/activate")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult Activate(
        Guid sessionId,
        DeviceExamTransitionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProfileSha256))
        {
            return BadRequest();
        }

        var status = examSessionStore.ActivateForDevice(
            sessionId,
            request.ProfileSha256);
        return status switch
        {
            ExamSessionActivationStatus.Activated => NoContent(),
            ExamSessionActivationStatus.NotFound => NotFound(),
            ExamSessionActivationStatus.Conflict => Conflict(),
            _ => throw new InvalidOperationException(
                $"Unknown activation status '{status}'."),
        };
    }

    [HttpPost("{sessionId:guid}/complete")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public IActionResult Complete(
        Guid sessionId,
        DeviceExamTransitionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProfileSha256))
        {
            return BadRequest();
        }

        var status = examSessionStore.CompleteForDevice(
            sessionId,
            request.ProfileSha256);
        return status switch
        {
            ExamSessionCompletionStatus.Completed => NoContent(),
            ExamSessionCompletionStatus.NotFound => NotFound(),
            ExamSessionCompletionStatus.Conflict => Conflict(),
            _ => throw new InvalidOperationException(
                $"Unknown completion status '{status}'."),
        };
    }
}
