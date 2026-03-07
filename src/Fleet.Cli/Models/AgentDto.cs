using Shared.Contracts.Models;

namespace Fleet.Cli.Models;

/// <summary>
/// Client-side response model mirroring AgentState from CloudConsole.Api.
/// Lives in the CLI because AgentState is not in Shared.Contracts.
/// </summary>
public sealed record AgentDto(
    string AgentId,
    string Hostname,
    string OsDescription,
    bool IsOnline,
    DateTime LastSeen,
    double CpuPercent,
    double MemoryUsedMb,
    double DiskUsedPercent,
    List<ManagedApp> ManagedApps
);
