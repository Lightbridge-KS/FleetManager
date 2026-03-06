using Microsoft.AspNetCore.SignalR;
using CloudConsole.Api.Hubs;
using CloudConsole.Api.Services;
using Shared.Contracts;
using Shared.Contracts.Messages;

var builder = WebApplication.CreateBuilder(args);

// ──────────────────────── Services ────────────────────────

// Serialize enums as strings (e.g. "Running") so the HTML dashboard can use them as CSS classes
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
        options.PayloadSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddSingleton<AgentRegistry>();  // In production: scoped + DB-backed

var app = builder.Build();

// ──────────────────────── Static Dashboard ────────────────────────

app.UseDefaultFiles();      // rewrites "/" to "/index.html"
app.UseStaticFiles();

// ──────────────────────── SignalR Hub ────────────────────────

app.MapHub<FleetHub>("/hub/fleet");

// ──────────────────────── REST API: Monitoring ────────────────────────

var api = app.MapGroup("/api");

// GET /api/agents — list all agents and their current state
api.MapGet("/agents", (AgentRegistry registry) =>
    Results.Ok(registry.GetAllAgents()));

// GET /api/agents/{agentId} — single agent detail
api.MapGet("/agents/{agentId}", (string agentId, AgentRegistry registry) =>
{
    var agent = registry.GetAgent(agentId);
    return agent is null ? Results.NotFound() : Results.Ok(agent);
});

// GET /api/audit — recent audit events
api.MapGet("/audit", (AgentRegistry registry) =>
    Results.Ok(registry.GetAuditLog()));

// ──────────────────────── REST API: Commands (Cloud → Agent) ────────────────────────

// POST /api/agents/{agentId}/restart-app
api.MapPost("/agents/{agentId}/restart-app", async (
    string agentId,
    RestartAppCommand command,
    AgentRegistry registry,
    IHubContext<FleetHub, IAgentClient> hubContext) =>
{
    var connId = registry.GetConnectionId(agentId);
    if (connId is null) return Results.NotFound("Agent not connected");

    // Send command to the specific agent via SignalR
    await hubContext.Clients.Client(connId).RestartApp(command);

    registry.AppendAudit(new(agentId, DateTime.UtcNow, "Command",
        $"RestartApp sent for {command.AppId}"));

    return Results.Accepted();
});

// POST /api/agents/{agentId}/push-config
api.MapPost("/agents/{agentId}/push-config", async (
    string agentId,
    PushConfigCommand command,
    AgentRegistry registry,
    IHubContext<FleetHub, IAgentClient> hubContext) =>
{
    var connId = registry.GetConnectionId(agentId);
    if (connId is null) return Results.NotFound("Agent not connected");

    await hubContext.Clients.Client(connId).PushConfig(command);

    registry.AppendAudit(new(agentId, DateTime.UtcNow, "Command",
        $"PushConfig sent for {command.AppId} with {command.Settings.Count} settings"));

    return Results.Accepted();
});

// POST /api/agents/{agentId}/update-app
api.MapPost("/agents/{agentId}/update-app", async (
    string agentId,
    UpdateAppCommand command,
    AgentRegistry registry,
    IHubContext<FleetHub, IAgentClient> hubContext) =>
{
    var connId = registry.GetConnectionId(agentId);
    if (connId is null) return Results.NotFound("Agent not connected");

    await hubContext.Clients.Client(connId).UpdateApp(command);

    registry.AppendAudit(new(agentId, DateTime.UtcNow, "Command",
        $"UpdateApp sent for {command.AppId} → v{command.TargetVersion}"));

    return Results.Accepted();
});

app.Run();
