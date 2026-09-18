using ExamKiosk.DeviceAgent;
using ExamKiosk.DeviceAgent.WindowsConfiguration;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
	options.ServiceName = "Exam Kiosk Device Agent";
});
builder.Services.AddSingleton<WindowsConfigurationCompiler>();
builder.Services.AddSingleton<TransitionManager>();
builder.Services.AddHostedService<AgentWorker>();

var host = builder.Build();
host.Run();
