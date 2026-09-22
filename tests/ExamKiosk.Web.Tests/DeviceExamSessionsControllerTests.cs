using System.Reflection;
using static ExamKiosk.Web.Tests.EffectiveProfileTestData;
using ExamKiosk.Contracts;
using ExamKiosk.Web.Authentication;
using ExamKiosk.Web.Controllers;
using ExamKiosk.Web.Controllers.Models;
using ExamKiosk.Web.ExamSessions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamKiosk.Web.Tests;

public sealed class DeviceExamSessionsControllerTests
{
    [Fact]
    public void Controller_RequiresAgentBearerRole()
    {
        var authorization = typeof(DeviceExamSessionsController)
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorization);
        Assert.Equal(
            AgentAuthorization.AuthenticationScheme,
            authorization.AuthenticationSchemes);
        Assert.Equal(AgentAuthorization.AppRole, authorization.Roles);
    }

    [Fact]
    public void ActivateAndComplete_WithMatchingReceipt_TransitionSession()
    {
        var store = new InMemoryExamSessionStore();
        var profile = CreateProfile();
        var started = store.Start("student@example.com", profile);
        var controller = new DeviceExamSessionsController(store);
        var request = new DeviceExamTransitionRequest(
            EffectiveProfileDigest.Compute(profile));

        var activation = controller.Activate(
            started.SessionId,
            request);
        var completion = controller.Complete(
            started.SessionId,
            request);

        Assert.IsType<NoContentResult>(activation);
        Assert.IsType<NoContentResult>(completion);
        Assert.Equal(
            ExamSessionState.Completed,
            store.Get(started.SessionId)?.State);
    }

    [Fact]
    public void Activate_WithMismatchedReceipt_ReturnsNotFound()
    {
        var store = new InMemoryExamSessionStore();
        var started = store.Start(
            "student@example.com",
            CreateProfile());
        var controller = new DeviceExamSessionsController(store);

        var result = controller.Activate(
            started.SessionId,
            new DeviceExamTransitionRequest(new string('0', 64)));

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal(
            ExamSessionState.Starting,
            store.Get(started.SessionId)?.State);
    }

}
