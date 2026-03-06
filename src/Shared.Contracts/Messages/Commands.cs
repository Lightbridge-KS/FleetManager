namespace Shared.Contracts.Messages;

/// <summary>
/// Command to push a configuration change to a specific app on the agent.
/// Direction: Cloud → Agent
/// </summary>
public sealed record PushConfigCommand(
    string AppId,
    Dictionary<string, string> Settings
);

/// <summary>
/// Command to restart a managed application.
/// Direction: Cloud → Agent
/// </summary>
public sealed record RestartAppCommand(
    string AppId
);

/// <summary>
/// Command to update a managed application to a new version.
/// Direction: Cloud → Agent
/// </summary>
public sealed record UpdateAppCommand(
    string AppId,
    string TargetVersion,
    string ArtifactUrl   // URL where the agent can download the new package
);

/// <summary>
/// Generic command result returned by the agent after executing a command.
/// Direction: Agent → Cloud
/// </summary>
public sealed record CommandResult(
    string AgentId,
    string CommandType,
    bool Success,
    string Message
);
