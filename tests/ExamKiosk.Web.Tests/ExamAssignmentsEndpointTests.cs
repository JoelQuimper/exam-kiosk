using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using ExamKiosk.Web.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamKiosk.Web.Tests;

public sealed class ExamAssignmentsEndpointTests
{
    [Fact]
    public async Task Get_WhenAnonymous_ReturnsUnauthorizedWithoutRedirect()
    {
        await using var application = CreateApplication(authenticated: false);
        using var client = application.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/v1/exam-assignments");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Get_WhenUpnClaimIsMissing_ReturnsForbidden()
    {
        await using var application = CreateApplication(
            authenticated: true,
            userPrincipalName: null);
        using var client = application.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/v1/exam-assignments");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AssignedExamCases))]
    public async Task Get_ReturnsOnlyAuthenticatedStudentsAssignments(
        string userPrincipalName,
        string[] expectedAssignmentIds,
        string[][] expectedToolIds)
    {
        await using var application = CreateApplication(
            authenticated: true,
            userPrincipalName);
        using var client = application.CreateClient();

        var response = await client.GetAsync("/api/v1/exam-assignments");
        var assignments = await response.Content
            .ReadFromJsonAsync<ExamAssignmentResponse[]>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(assignments);
        Assert.Equal(
            expectedAssignmentIds,
            assignments.Select(assignment => assignment.AssignmentId));
        Assert.Equal(expectedToolIds.Length, assignments.Length);
        for (var index = 0; index < expectedToolIds.Length; index++)
        {
            Assert.Equal(
                expectedToolIds[index],
                assignments[index].Tools.Select(tool => tool.Id));
        }
    }

    [Fact]
    public async Task Get_MatchesUpnCaseInsensitively()
    {
        await using var application = CreateApplication(
            authenticated: true,
            "STUDENT1@JQDEV.ONMICROSOFT.COM");
        using var client = application.CreateClient();

        var assignments = await client.GetFromJsonAsync<ExamAssignmentResponse[]>(
            "/api/v1/exam-assignments");

        Assert.Equal(
            ["student1-exam1", "student1-exam2"],
            assignments!.Select(assignment => assignment.AssignmentId));
    }

    [Fact]
    public async Task Get_DoesNotExposeProfileEnforcementData()
    {
        await using var application = CreateApplication(
            authenticated: true,
            "student3@jqdev.onmicrosoft.com");
        using var client = application.CreateClient();

        var response = await client.GetAsync("/api/v1/exam-assignments");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("sharePoint", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jqdev.sharepoint.com", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("path", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("appUserModelId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("requiredWebDestinations", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Get_ReturnsConfiguredExamAndToolDisplayData()
    {
        await using var application = CreateApplication(
            authenticated: true,
            "student3@jqdev.onmicrosoft.com");
        using var client = application.CreateClient();

        var assignments = await client.GetFromJsonAsync<ExamAssignmentResponse[]>(
            "/api/v1/exam-assignments");

        var assignment = Assert.Single(assignments!);
        Assert.Equal("exam-2", assignment.Exam.Id);
        Assert.Equal(
            "Sciences secondaire 4 — Analyse de données",
            assignment.Exam.Title);
        Assert.Equal("chart", assignment.Exam.Icon);
        Assert.Equal(
            ["Microsoft Word", "Calculator", "Dictionnaire Usito"],
            assignment.Tools.Select(tool => tool.DisplayName));
        Assert.Equal(
            ["word", "calculator", "dictionary"],
            assignment.Tools.Select(tool => tool.Icon));
    }

    [Fact]
    public void UpnResolver_FallsBackToStandardUpnClaim()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.Upn, " student1@jqdev.onmicrosoft.com ")],
                AssignmentAuthenticationHandler.SchemeName));

        var userPrincipalName = AuthenticatedUpnResolver.Resolve(principal);

        Assert.Equal("student1@jqdev.onmicrosoft.com", userPrincipalName);
    }

    public static TheoryData<string, string[], string[][]> AssignedExamCases =>
        new()
        {
            {
                "student1@jqdev.onmicrosoft.com",
                ["student1-exam1", "student1-exam2"],
                [["windows-calculator"], []]
            },
            {
                "student2@jqdev.onmicrosoft.com",
                ["student2-exam1"],
                [["microsoft-word"]]
            },
            {
                "student3@jqdev.onmicrosoft.com",
                ["student3-exam2"],
                [["microsoft-word", "windows-calculator", "usito-dictionary"]]
            },
            {
                "student4@jqdev.onmicrosoft.com",
                [],
                []
            },
            {
                "unknown@jqdev.onmicrosoft.com",
                [],
                []
            },
        };

    private static WebApplicationFactory<Program> CreateApplication(
        bool authenticated,
        string? userPrincipalName = null)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            HomePageTests.ConfigureTestEntraSettings(builder);
            builder.ConfigureTestServices(services =>
            {
                services
                    .AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme =
                            AssignmentAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme =
                            AssignmentAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<
                        AssignmentAuthenticationOptions,
                        AssignmentAuthenticationHandler>(
                        AssignmentAuthenticationHandler.SchemeName,
                        options =>
                        {
                            options.Authenticated = authenticated;
                            options.UserPrincipalName = userPrincipalName;
                        });
            });
        });
    }

    private sealed class AssignmentAuthenticationOptions
        : AuthenticationSchemeOptions
    {
        public bool Authenticated { get; set; }

        public string? UserPrincipalName { get; set; }
    }

    private sealed class AssignmentAuthenticationHandler(
        IOptionsMonitor<AssignmentAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AssignmentAuthenticationOptions>(
            options,
            logger,
            encoder)
    {
        internal const string SchemeName = "AssignmentTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Options.Authenticated)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, "test-student"),
            };
            if (Options.UserPrincipalName is not null)
            {
                claims.Add(new("preferred_username", Options.UserPrincipalName));
            }

            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        protected override Task HandleChallengeAsync(
            AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status302Found;
            Response.Headers.Location = "/identity-provider";
            return Task.CompletedTask;
        }
    }

    private sealed record ExamAssignmentResponse(
        string AssignmentId,
        ExamResponse Exam,
        ToolResponse[] Tools);

    private sealed record ExamResponse(
        string Id,
        string Title,
        string Icon);

    private sealed record ToolResponse(
        string Id,
        string DisplayName,
        string Icon);
}
