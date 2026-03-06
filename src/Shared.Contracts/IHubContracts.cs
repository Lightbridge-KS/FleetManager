using Shared.Contracts.Models;
using Shared.Contracts.Messages;

namespace Shared.Contracts;

/// <summary>
/// Methods the CLOUD HUB exposes — agents call these.
/// (Agent → Cloud direction)
/// </summary>
public interface ICloudHub
{
    Task RegisterAgent(AgentRegistration registration);
    Task SendHeartbeat(Heartbeat heartbeat);
    Task ReportAuditEvent(AuditEvent auditEvent);
    Task ReportCommandResult(CommandResult result);
}

/// <summary>
/// Methods the AGENT exposes — cloud calls these via SignalR client proxy.
/// (Cloud → Agent direction)
/// </summary>
public interface IAgentClient
{
    Task PushConfig(PushConfigCommand command);
    Task RestartApp(RestartAppCommand command);
    Task UpdateApp(UpdateAppCommand command);
    Task RequestHeartbeat();  // "ping" the agent to send a heartbeat now
}
