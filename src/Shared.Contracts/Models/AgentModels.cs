namespace Shared.Contracts.Models;

/// <summary>
/// Represents the registration info an agent sends when it first connects to the cloud console.
/// </summary>
public sealed record AgentRegistration(
    string AgentId,
    string Hostname,
    string OsDescription,
    List<ManagedApp> ManagedApps
);

/// <summary>
/// A locally-installed application that the agent manages on behalf of the cloud console.
/// </summary>
public sealed record ManagedApp(
    string AppId,
    string Name,
    string Version,
    AppStatus Status
);

/// <summary>
/// Periodic heartbeat payload sent from agent → cloud.
/// </summary>
public sealed record Heartbeat(
    string AgentId,
    DateTime TimestampUtc,
    double CpuPercent,
    double MemoryUsedMb,
    double DiskUsedPercent,
    List<ManagedApp> ManagedApps
);

/// <summary>
/// An auditable event that happened on the agent side.
/// </summary>
public sealed record AuditEvent(
    string AgentId,
    DateTime TimestampUtc,
    string Category,   // e.g. "Config", "Update", "AppLifecycle"
    string Message
);

public enum AppStatus
{
    Running,
    Stopped,
    Error,
    Updating
}
