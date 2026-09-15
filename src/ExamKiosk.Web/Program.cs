using System.Globalization;
using ExamKiosk.Web.Components;
using ExamKiosk.Web.Exams;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Localization;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));
builder.Services.Configure<OpenIdConnectOptions>(
    OpenIdConnectDefaults.AuthenticationScheme,
    options =>
    {
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;
        options.Events.OnRemoteFailure = context =>
        {
            context.HandleResponse();
            context.Response.Redirect("/sign-in-required");
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddControllers();
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    CultureInfo[] supportedCultures = [new("en"), new("fr")];
    options.DefaultRequestCulture = new RequestCulture("en");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
    options.RequestCultureProviders =
    [
        new AcceptLanguageHeaderRequestCultureProvider(),
    ];
});
builder.Services.AddRazorComponents();
builder.Services.AddSingleton<IExamCatalog, ExamCatalog>();

var app = builder.Build();

app.UseStaticFiles();
app.UseRequestLocalization();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapControllers();
app.MapGet(
        "/authentication/login",
        (string? returnUrl) =>
            Results.Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = NormalizeReturnUrl(returnUrl),
                }))
    .AllowAnonymous();
app.MapPost(
        "/authentication/logout",
        async Task<IResult> (HttpContext context, IAntiforgery antiforgery) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.BadRequest();
            }

            return Results.SignOut(
                new AuthenticationProperties { RedirectUri = "/" },
                [
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    OpenIdConnectDefaults.AuthenticationScheme,
                ]);
        })
    .RequireAuthorization();
app.MapRazorComponents<App>();

app.Run();

static string NormalizeReturnUrl(string? returnUrl)
{
    if (string.IsNullOrWhiteSpace(returnUrl)
        || !returnUrl.StartsWith('/')
        || returnUrl.StartsWith("//", StringComparison.Ordinal)
        || returnUrl.StartsWith("/\\", StringComparison.Ordinal))
    {
        return "/";
    }

    return returnUrl;
}

public partial class Program;