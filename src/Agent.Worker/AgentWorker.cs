using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.SignalR.Client;
using Shared.Contracts;
using Shared.Contracts.Models;
using Agent.Worker.Services;
using Agent.Worker.Handlers;

namespace Agent.Worker;

/// <summary>
/// The core background service running on each Ubuntu server.
/// 
/// Lifecycle:
///   1. Build SignalR connection to the cloud console
///   2. Register command handlers (cloud → agent)
///   3. Connect and register this agent
///   4. Loop: send heartbeat every N seconds
///   5. On shutdown: graceful disconnect
/// </summary>
public sealed class AgentWorker : BackgroundService
{
    private readonly ILogger<AgentWorker> _logger;
    private readonly LocalAppManager _appManager;
    private readonly IConfiguration _config;

    public AgentWorker(
        ILogger<AgentWorker> logger,
        LocalAppManager appManager,
        IConfiguration config)
    {
        _logger = logger;
        _appManager = appManager;
        _config = config;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var hubUrl = _config["CloudConsole:HubUrl"] ?? "http://localhost:5000/hub/fleet";
        var agentId = _config["Agent:Id"] ?? $"agent-{Environment.MachineName}";
        var heartbeatIntervalSec = int.Parse(_config["Agent:HeartbeatIntervalSec"] ?? "10");

        _logger.LogInformation("Agent {AgentId} starting — connecting to {Hub}", agentId, hubUrl);

        // ── 1. Build the SignalR connection with auto-reconnect ──
        var connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2),
                                            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
            .Build();

        // ── 2. Register command handlers (cloud → agent direction) ──
        CommandHandlerRegistration.RegisterHandlers(connection, _appManager, agentId, _logger);

        connection.Reconnected += async connectionId =>
        {
            _logger.LogInformation("Reconnected to cloud (connId={ConnId}), re-registering...", connectionId);
            await RegisterSelf(connection, agentId);
        };

        // ── 3. Connect with retry ──
        await ConnectWithRetryAsync(connection, stoppingToken);

        // ── 4. Register this agent with the cloud ──
        await RegisterSelf(connection, agentId);

        // ── 5. Heartbeat loop ──
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var heartbeat = BuildHeartbeat(agentId);
                await connection.InvokeAsync(nameof(ICloudHub.SendHeartbeat), heartbeat, stoppingToken);
                _logger.LogDebug("Heartbeat sent (CPU={Cpu}%)", heartbeat.CpuPercent);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send heartbeat");
            }

            await Task.Delay(TimeSpan.FromSeconds(heartbeatIntervalSec), stoppingToken);
        }

        // ── 6. Graceful shutdown ──
        await connection.StopAsync(CancellationToken.None);
        _logger.LogInformation("Agent {AgentId} stopped", agentId);
    }

    // ──────────────────────── Helper Methods ────────────────────────

    private async Task RegisterSelf(HubConnection connection, string agentId)
    {
        var registration = new AgentRegistration(
            AgentId: agentId,
            Hostname: Environment.MachineName,
            OsDescription: RuntimeInformation.OSDescription,
            ManagedApps: _appManager.GetManagedApps()
        );

        await connection.InvokeAsync(nameof(ICloudHub.RegisterAgent), registration);
        _logger.LogInformation("Registered with cloud as {AgentId}", agentId);
    }

    private Heartbeat BuildHeartbeat(string agentId)
    {
        // Gather real(ish) system metrics
        var process = Process.GetCurrentProcess();
        var cpuPercent = Random.Shared.NextDouble() * 40 + 10;  // simulated; see note below
        var memoryMb = process.WorkingSet64 / (1024.0 * 1024.0);

        // Note: Real CPU % on Linux requires reading /proc/stat over an interval.
        //       For this demo we simulate it. A production agent would use
        //       System.Diagnostics.PerformanceCounter or read /proc directly.

        return new Heartbeat(
            AgentId: agentId,
            TimestampUtc: DateTime.UtcNow,
            CpuPercent: Math.Round(cpuPercent, 1),
            MemoryUsedMb: Math.Round(memoryMb, 1),
            DiskUsedPercent: Math.Round(Random.Shared.NextDouble() * 30 + 40, 1),  // simulated
            ManagedApps: _appManager.GetManagedApps()
        );
    }

    private async Task ConnectWithRetryAsync(HubConnection connection, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await connection.StartAsync(ct);
                _logger.LogInformation("Connected to cloud hub");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Connection failed ({Message}), retrying in 5s...", ex.Message);
                await Task.Delay(5000, ct);
            }
        }
    }
}
