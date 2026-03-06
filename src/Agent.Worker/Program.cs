using Agent.Worker;
using Agent.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

// Integrate with systemd on Linux (graceful start/stop, watchdog)
builder.Services.AddSystemd();

// Register services
builder.Services.AddSingleton<LocalAppManager>();
builder.Services.AddHostedService<AgentWorker>();

var host = builder.Build();
host.Run();
