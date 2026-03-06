using Microsoft.AspNetCore.SignalR;
using Shared.Contracts;
using Shared.Contracts.Models;
using Shared.Contracts.Messages;
using CloudConsole.Api.Services;

namespace CloudConsole.Api.Hubs;

/// <summary>
/// The central SignalR hub that agents connect to.
/// 
/// Data flow:
///   Agent → Hub:   RegisterAgent, SendHeartbeat, ReportAuditEvent, ReportCommandResult
///   Hub → Agent:   PushConfig, RestartApp, UpdateApp, RequestHeartbeat  (via IAgentClient)
/// </summary>
public sealed class FleetHub : Hub<IAgentClient>, ICloudHub
{
    private readonly AgentRegistry _registry;
    private readonly ILogger<FleetHub> _logger;

    public FleetHub(AgentRegistry registry, ILogger<FleetHub> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    // ──────────────────────── Lifecycle ────────────────────────

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _registry.RemoveConnection(Context.ConnectionId);
        _logger.LogInformation("Agent disconnected: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    // ──────────────────────── Agent → Cloud ────────────────────────

    /// <summary>
    /// Called once when an agent first connects. Registers it in the fleet.
    /// </summary>
    public Task RegisterAgent(AgentRegistration registration)
    {
        _registry.RegisterOrUpdate(registration, Context.ConnectionId);
        _logger.LogInformation(
            "Agent registered: {AgentId} ({Hostname}) with {AppCount} apps",
            registration.AgentId, registration.Hostname, registration.ManagedApps.Count);

        _registry.AppendAudit(new AuditEvent(
            registration.AgentId, DateTime.UtcNow, "Lifecycle", "Agent registered"));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Called periodically by the agent to report health and app status.
    /// </summary>
    public Task SendHeartbeat(Heartbeat heartbeat)
    {
        _registry.UpdateHeartbeat(heartbeat);
        _logger.LogDebug(
            "Heartbeat from {AgentId}: CPU={Cpu}%, MEM={Mem}MB",
            heartbeat.AgentId, heartbeat.CpuPercent, heartbeat.MemoryUsedMb);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Agent reports an auditable event (config change, app restart, update, etc.)
    /// </summary>
    public Task ReportAuditEvent(AuditEvent auditEvent)
    {
        _registry.AppendAudit(auditEvent);
        _logger.LogInformation(
            "Audit [{Category}] from {AgentId}: {Message}",
            auditEvent.Category, auditEvent.AgentId, auditEvent.Message);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Agent reports the result of a command that was previously pushed to it.
    /// </summary>
    public Task ReportCommandResult(CommandResult result)
    {
        _logger.LogInformation(
            "Command result from {AgentId}: {CommandType} → {Success} ({Message})",
            result.AgentId, result.CommandType, result.Success, result.Message);

        _registry.AppendAudit(new AuditEvent(
            result.AgentId, DateTime.UtcNow, "CommandResult",
            $"{result.CommandType}: {(result.Success ? "OK" : "FAIL")} - {result.Message}"));

        return Task.CompletedTask;
    }
}
