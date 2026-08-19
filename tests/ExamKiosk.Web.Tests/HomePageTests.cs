using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ExamKiosk.Web.Tests;

public sealed class HomePageTests(WebApplicationFactory<Program> application)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Home_RendersExamKioskPage()
    {
        using var client = application.CreateClient();

        var response = await client.GetAsync("/");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<h1>Exam Kiosk</h1>", content);
    }
}