using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using ExamKiosk.Web.Exams;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace ExamKiosk.Web.Tests;

public sealed class HomePageTests
{
    [Fact]
    public async Task Home_WhenAnonymous_RedirectsToSignIn()
    {
        await using var application = CreateApplication(authenticated: false);
        using var client = application.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(
            "/authentication/login?returnUrl=%2F",
            response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Exams_WhenAnonymous_RedirectsToSignIn()
    {
        await using var application = CreateApplication(authenticated: false);
        using var client = application.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/exams");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(
            "/authentication/login?returnUrl=%2Fexams",
            response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Launcher_WhenAnonymous_RedirectsToSignIn()
    {
        await using var application = CreateApplication(authenticated: false);
        using var client = application.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/launcher");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(
            "/authentication/login?returnUrl=%2Flauncher",
            response.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/exams")]
    public async Task ExamList_WhenAuthenticated_RendersGreetingAndAssignedExam(string path)
    {
        await using var application = CreateApplication(authenticated: true);
        using var client = application.CreateClient();

        var response = await client.GetAsync(path);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<h1>Hi Joel, here are your exams</h1>", content);
        Assert.Contains("Joel Student", content);
        Assert.Contains("Bogus exam", content);
        Assert.Contains("aria-hidden=\"true\">+/-</div>", content);
        Assert.Contains("<dt>Tools</dt>", content);
        Assert.Contains("Microsoft Edge", content);
        Assert.Contains("Calculator", content);
        Assert.DoesNotContain("<dt>Format</dt>", content);
        Assert.Contains("Exam launching will be enabled in the desktop app.", content);
    }

    [Fact]
    public async Task SignInRequired_RendersRecoveryPageWithoutAuthentication()
    {
        await using var application = CreateApplication(authenticated: false);
        using var client = application.CreateClient();

        var response = await client.GetAsync("/sign-in-required");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("You must sign in to view your exams.", content);
        Assert.Contains("/authentication/login?returnUrl=%2F", content);
    }

    [Fact]
    public async Task ExamList_WhenProfileAllowsNoTools_RendersNone()
    {
        await using var application = CreateApplication(
            authenticated: true,
            allowedTools: []);
        using var client = application.CreateClient();

        var response = await client.GetAsync("/");
        var content = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<dt>Tools</dt>", content);
        Assert.Contains("<span>None</span>", content);
    }

    [Fact]
    public async Task Launcher_WhenAuthenticated_RendersNativeBridgeControls()
    {
        await using var application = CreateApplication(authenticated: true);
        using var client = application.CreateClient();

        var response = await client.GetAsync("/launcher");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"launcher-start-exam\"", content);
        Assert.Contains("id=\"launcher-status\"", content);
        Assert.Contains("launcher-bridge.js", content);
        Assert.DoesNotContain("Exam launching will be enabled in the desktop app.", content);
    }

    [Fact]
    public async Task ExamSession_WhenAnonymous_RendersRestrictedClientBridge()
    {
        await using var application = CreateApplication(authenticated: false);
        using var client = application.CreateClient();

        var response = await client.GetAsync("/exam-session");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<h1>Exam in progress</h1>", content);
        Assert.Contains("id=\"session-open-exam\"", content);
        Assert.Contains("id=\"session-finish-exam\"", content);
        Assert.Contains("id=\"session-status\"", content);
        Assert.Contains("exam-session-bridge.js", content);
        Assert.DoesNotContain("Bogus exam", content);
        Assert.DoesNotContain("Microsoft Edge</li>", content);
        Assert.DoesNotContain("/authentication/login", content);
    }

    [Fact]
    public async Task ExamSession_WhenBrowserPrefersFrench_RendersFrench()
    {
        await using var application = CreateApplication(authenticated: false);
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("fr");

        var response = await client.GetAsync("/exam-session");
        var content = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Examen en cours", content);
        Assert.Contains("Ouvrir l'examen", content);
        Assert.Contains("Examen terminé", content);
    }

    [Fact]
    public async Task ExamList_WhenBrowserPrefersFrench_RendersFrench()
    {
        await using var application = CreateApplication(authenticated: true);
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("fr");

        var response = await client.GetAsync("/");
        var content = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<html lang=\"fr\">", content);
        Assert.Contains("<h1>Bonjour Joel, voici vos examens</h1>", content);
        Assert.Contains("Examen fictif", content);
        Assert.Contains("<dt>Outils</dt>", content);
        Assert.Contains("Calculatrice", content);
        Assert.Contains("Se déconnecter", content);
    }

    [Fact]
    public async Task SignInRequired_WhenBrowserPrefersFrench_RendersFrench()
    {
        await using var application = CreateApplication(authenticated: false);
        using var client = application.CreateClient();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("fr");

        var response = await client.GetAsync("/sign-in-required?culture=en");
        var content = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Vous devez vous connecter pour voir vos examens.", content);
        Assert.Contains("Se connecter", content);
        Assert.DoesNotContain("You must sign in to view your exams.", content);
    }

    [Fact]
    public async Task AuthenticationFailure_RedirectsToSignInRequired()
    {
        await using var application = CreateApplication(authenticated: false);
        var options = application.Services
            .GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);
        var httpContext = new DefaultHttpContext();
        var scheme = new AuthenticationScheme(
            OpenIdConnectDefaults.AuthenticationScheme,
            displayName: null,
            typeof(OpenIdConnectHandler));
        var failureContext = new RemoteFailureContext(
            httpContext,
            scheme,
            options,
            new InvalidOperationException("Authentication canceled."));

        await options.Events.RemoteFailure(failureContext);

        Assert.Equal(StatusCodes.Status302Found, httpContext.Response.StatusCode);
        Assert.Equal("/sign-in-required", httpContext.Response.Headers.Location);
    }

    [Fact]
    public void Authentication_UsesAuthorizationCodeFlowWithPkce()
    {
        using var application = CreateApplication(authenticated: false);
        var options = application.Services
            .GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);

        Assert.Equal(OpenIdConnectResponseType.Code, options.ResponseType);
        Assert.True(options.UsePkce);
    }

    [Fact]
    public async Task Logout_WithoutAntiforgeryToken_IsRejected()
    {
        await using var application = CreateApplication(authenticated: true);
        using var client = application.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var form = new FormUrlEncodedContent([]);
        var response = await client.PostAsync("/authentication/logout", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("https://attacker.example", "/")]
    [InlineData("//attacker.example", "/")]
    [InlineData("/exams", "/exams")]
    public async Task Login_NormalizesReturnUrl(string? returnUrl, string expectedReturnUrl)
    {
        await using var application = CreateApplication(authenticated: false);
        using var client = application.CreateClient(
            new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var requestUri = returnUrl is null
            ? "/authentication/login"
            : $"/authentication/login?returnUrl={Uri.EscapeDataString(returnUrl)}";

        var response = await client.GetAsync(requestUri);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expectedReturnUrl, response.Headers.Location?.OriginalString);
    }

    private static WebApplicationFactory<Program> CreateApplication(
        bool authenticated,
        IReadOnlyList<string>? allowedTools = null)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            ConfigureTestEntraSettings(builder);
            builder.ConfigureTestServices(services =>
            {
                if (allowedTools is not null)
                {
                    services.AddSingleton<IExamCatalog>(new TestExamCatalog(allowedTools));
                }

                services
                    .AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName,
                        options => options.ClaimsIssuer = authenticated ? "authenticated" : null);
            });
        });
    }

    private sealed class TestExamCatalog(IReadOnlyList<string> allowedTools) : IExamCatalog
    {
        public IReadOnlyList<ExamSummary> GetAssignedExams() =>
        [
            new(
                "Profile test exam",
                "Test course",
                "Tests the effective profile.",
                60,
                "Available now",
                allowedTools,
                "Ready"),
        ];
    }

    internal static void ConfigureTestEntraSettings(IWebHostBuilder builder)
    {
        builder.UseSetting("AzureAd:Instance", "https://login.microsoftonline.com/");
        builder.UseSetting("AzureAd:TenantId", "11111111-1111-1111-1111-111111111111");
        builder.UseSetting("AzureAd:ClientId", "22222222-2222-2222-2222-222222222222");
        builder.UseSetting("AzureAd:ClientSecret", "integration-test-only");
        builder.UseSetting("AzureAd:CallbackPath", "/signin-oidc");
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        internal const string SchemeName = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Options.ClaimsIssuer is null)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            Claim[] claims =
            [
                new(ClaimTypes.NameIdentifier, "student-1"),
                new(ClaimTypes.Name, "joel@example.edu"),
                new(ClaimTypes.GivenName, "Joel"),
                new("name", "Joel Student"),
            ];
            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status302Found;
            Response.Headers.Location = properties.RedirectUri
                ?? $"/authentication/login?returnUrl={Uri.EscapeDataString(Request.Path)}";
            return Task.CompletedTask;
        }
    }
}