using System.Collections.Concurrent;
using Shared.Contracts.Models;

namespace CloudConsole.Api.Services;

/// <summary>
/// Thread-safe, in-memory store of all connected agents and their latest state.
/// In production, back this with a database (PostgreSQL + EF Core).
/// </summary>
public sealed class AgentRegistry
{
    // AgentId → state
    private readonly ConcurrentDictionary<string, AgentState> _agents = new();

    // AgentId → SignalR ConnectionId (for targeted commands)
    private readonly ConcurrentDictionary<string, string> _connections = new();

    // Append-only audit log (in production: database table)
    private readonly ConcurrentBag<AuditEvent> _auditLog = [];

    // ──────────────────────── Connection Tracking ────────────────────────

    public void MapConnection(string agentId, string connectionId)
        => _connections[agentId] = connectionId;

    public void RemoveConnection(string connectionId)
    {
        var entry = _connections.FirstOrDefault(kv => kv.Value == connectionId);
        if (entry.Key is not null)
        {
            _connections.TryRemove(entry.Key, out _);
            if (_agents.TryGetValue(entry.Key, out var state))
                state.IsOnline = false;
        }
    }

    public string? GetConnectionId(string agentId)
        => _connections.GetValueOrDefault(agentId);

    // ──────────────────────── Agent State ────────────────────────

    public void RegisterOrUpdate(AgentRegistration reg, string connectionId)
    {
        var state = _agents.GetOrAdd(reg.AgentId, _ => new AgentState());
        state.AgentId = reg.AgentId;
        state.Hostname = reg.Hostname;
        state.OsDescription = reg.OsDescription;
        state.ManagedApps = reg.ManagedApps;
        state.IsOnline = true;
        state.LastSeen = DateTime.UtcNow;

        MapConnection(reg.AgentId, connectionId);
    }

    public void UpdateHeartbeat(Heartbeat hb)
    {
        if (!_agents.TryGetValue(hb.AgentId, out var state)) return;

        state.CpuPercent = hb.CpuPercent;
        state.MemoryUsedMb = hb.MemoryUsedMb;
        state.DiskUsedPercent = hb.DiskUsedPercent;
        state.ManagedApps = hb.ManagedApps;
        state.LastSeen = hb.TimestampUtc;
        state.IsOnline = true;
    }

    public IReadOnlyList<AgentState> GetAllAgents()
        => _agents.Values.ToList().AsReadOnly();

    public AgentState? GetAgent(string agentId)
        => _agents.GetValueOrDefault(agentId);

    // ──────────────────────── Audit Log ────────────────────────

    public void AppendAudit(AuditEvent evt) => _auditLog.Add(evt);

    public IReadOnlyList<AuditEvent> GetAuditLog(int take = 50)
        => _auditLog
            .OrderByDescending(e => e.TimestampUtc)
            .Take(take)
            .ToList()
            .AsReadOnly();
}

/// <summary>
/// Mutable view-model for dashboard consumption.
/// </summary>
public sealed class AgentState
{
    public string AgentId { get; set; } = "";
    public string Hostname { get; set; } = "";
    public string OsDescription { get; set; } = "";
    public bool IsOnline { get; set; }
    public DateTime LastSeen { get; set; }
    public double CpuPercent { get; set; }
    public double MemoryUsedMb { get; set; }
    public double DiskUsedPercent { get; set; }
    public List<ManagedApp> ManagedApps { get; set; } = [];
}
