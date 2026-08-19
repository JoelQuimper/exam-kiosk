using ExamKiosk.Web.Components;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddRazorComponents();

var app = builder.Build();

app.UseAntiforgery();

app.MapControllers();
app.MapRazorComponents<App>();

app.Run();

public partial class Program;