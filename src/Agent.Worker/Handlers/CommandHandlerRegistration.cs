using Microsoft.AspNetCore.SignalR.Client;
using Shared.Contracts;
using Shared.Contracts.Messages;
using Shared.Contracts.Models;
using Agent.Worker.Services;

namespace Agent.Worker.Handlers;

/// <summary>
/// Registers SignalR client-side handlers for commands pushed from the cloud console.
/// Each handler delegates to <see cref="LocalAppManager"/> and reports the result back.
/// 
/// Wire diagram:
///   Cloud  ──(SignalR)──►  CommandHandler  ──►  LocalAppManager
///                                │
///                                └──(SignalR)──►  Cloud (CommandResult)
/// </summary>
public static class CommandHandlerRegistration
{
    public static void RegisterHandlers(
        HubConnection connection,
        LocalAppManager appManager,
        string agentId,
        ILogger logger)
    {
        // ── PushConfig ──
        connection.On<PushConfigCommand>(nameof(IAgentClient.PushConfig), async command =>
        {
            logger.LogInformation("Received PushConfig for {AppId}", command.AppId);
            var ok = await appManager.ApplyConfigAsync(command.AppId, command.Settings);
            await connection.InvokeAsync(nameof(ICloudHub.ReportCommandResult),
                new CommandResult(agentId, "PushConfig", ok,
                    ok ? $"Applied {command.Settings.Count} settings to {command.AppId}"
                       : $"App {command.AppId} not found"));

            await connection.InvokeAsync(nameof(ICloudHub.ReportAuditEvent),
                new AuditEvent(agentId, DateTime.UtcNow, "Config",
                    $"Config pushed to {command.AppId}: {string.Join(", ", command.Settings.Keys)}"));
        });

        // ── RestartApp ──
        connection.On<RestartAppCommand>(nameof(IAgentClient.RestartApp), async command =>
        {
            logger.LogInformation("Received RestartApp for {AppId}", command.AppId);
            var ok = await appManager.RestartAppAsync(command.AppId);
            await connection.InvokeAsync(nameof(ICloudHub.ReportCommandResult),
                new CommandResult(agentId, "RestartApp", ok,
                    ok ? $"Restarted {command.AppId}" : $"App {command.AppId} not found"));

            await connection.InvokeAsync(nameof(ICloudHub.ReportAuditEvent),
                new AuditEvent(agentId, DateTime.UtcNow, "AppLifecycle",
                    $"App {command.AppId} restarted"));
        });

        // ── UpdateApp ──
        connection.On<UpdateAppCommand>(nameof(IAgentClient.UpdateApp), async command =>
        {
            logger.LogInformation("Received UpdateApp for {AppId} → v{Version}", command.AppId, command.TargetVersion);
            var ok = await appManager.UpdateAppAsync(command.AppId, command.TargetVersion, command.ArtifactUrl);
            await connection.InvokeAsync(nameof(ICloudHub.ReportCommandResult),
                new CommandResult(agentId, "UpdateApp", ok,
                    ok ? $"Updated {command.AppId} to v{command.TargetVersion}"
                       : $"Update failed for {command.AppId}"));

            await connection.InvokeAsync(nameof(ICloudHub.ReportAuditEvent),
                new AuditEvent(agentId, DateTime.UtcNow, "Update",
                    $"App {command.AppId} updated to v{command.TargetVersion}"));
        });

        // ── RequestHeartbeat (ping) ──
        connection.On(nameof(IAgentClient.RequestHeartbeat), () =>
        {
            logger.LogInformation("Cloud requested immediate heartbeat");
            // The HeartbeatWorker will pick this up, or we fire one now
        });
    }
}
