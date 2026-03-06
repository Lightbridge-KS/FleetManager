# FleetManager — OOP Architecture UML

This document describes the object-oriented architecture of FleetManager using Mermaid UML diagrams. It covers the class structure, project dependencies, communication flow, and runtime behaviour.

---

## Table of Contents

1. [Project Dependency Diagram](#project-dependency-diagram)
2. [Class Diagram — Shared.Contracts](#class-diagram--sharedcontracts)
3. [Class Diagram — CloudConsole.Api](#class-diagram--cloudconsoleapi)
4. [Class Diagram — Agent.Worker](#class-diagram--agentworker)
5. [Full Class Relationship Diagram](#full-class-relationship-diagram)
6. [Sequence Diagram — Agent Registration & Heartbeat](#sequence-diagram--agent-registration--heartbeat)
7. [Sequence Diagram — Cloud Command Execution](#sequence-diagram--cloud-command-execution)
8. [Component Diagram](#component-diagram)
9. [State Diagram — AppStatus](#state-diagram--appstatus)
10. [State Diagram — Agent Lifecycle](#state-diagram--agent-lifecycle)

---

## Project Dependency Diagram

Shows the compile-time dependency rule: both executable projects depend on `Shared.Contracts` but never reference each other.

```mermaid
graph LR
    CloudConsole["CloudConsole.Api<br/>(ASP.NET Core)"]
    Shared["Shared.Contracts<br/>(Class Library)"]
    Agent["Agent.Worker<br/>(Worker Service)"]

    CloudConsole -->|depends on| Shared
    Agent -->|depends on| Shared

    style Shared fill:#e0f0ff,stroke:#3388cc
    style CloudConsole fill:#d5f5d5,stroke:#33aa33
    style Agent fill:#fff3d5,stroke:#cc9933
```

---

## Class Diagram — Shared.Contracts

DTOs, hub interfaces, and enums shared by both cloud and agent projects.

```mermaid
classDiagram
    direction TB

    class AppStatus {
        <<enumeration>>
        Running
        Stopped
        Error
        Updating
    }

    class AgentRegistration {
        <<record>>
        +string AgentId
        +string Hostname
        +string OsDescription
        +List~ManagedApp~ ManagedApps
    }

    class ManagedApp {
        <<record>>
        +string AppId
        +string Name
        +string Version
        +AppStatus Status
    }

    class Heartbeat {
        <<record>>
        +string AgentId
        +DateTime TimestampUtc
        +double CpuPercent
        +double MemoryUsedMb
        +double DiskUsedPercent
        +List~ManagedApp~ ManagedApps
    }

    class AuditEvent {
        <<record>>
        +string AgentId
        +DateTime TimestampUtc
        +string Category
        +string Message
    }

    class PushConfigCommand {
        <<record>>
        +string AppId
        +Dictionary~string, string~ Settings
    }

    class RestartAppCommand {
        <<record>>
        +string AppId
    }

    class UpdateAppCommand {
        <<record>>
        +string AppId
        +string TargetVersion
        +string ArtifactUrl
    }

    class CommandResult {
        <<record>>
        +string AgentId
        +string CommandType
        +bool Success
        +string Message
    }

    class ICloudHub {
        <<interface>>
        +RegisterAgent(AgentRegistration registration) Task
        +SendHeartbeat(Heartbeat heartbeat) Task
        +ReportAuditEvent(AuditEvent auditEvent) Task
        +ReportCommandResult(CommandResult result) Task
    }

    class IAgentClient {
        <<interface>>
        +PushConfig(PushConfigCommand command) Task
        +RestartApp(RestartAppCommand command) Task
        +UpdateApp(UpdateAppCommand command) Task
        +RequestHeartbeat() Task
    }

    AgentRegistration --> ManagedApp : contains
    Heartbeat --> ManagedApp : contains
    ManagedApp --> AppStatus : uses
    ICloudHub ..> AgentRegistration : receives
    ICloudHub ..> Heartbeat : receives
    ICloudHub ..> AuditEvent : receives
    ICloudHub ..> CommandResult : receives
    IAgentClient ..> PushConfigCommand : sends
    IAgentClient ..> RestartAppCommand : sends
    IAgentClient ..> UpdateAppCommand : sends
```

---

## Class Diagram — CloudConsole.Api

The cloud-side server: SignalR hub, agent registry, and REST API endpoints.

```mermaid
classDiagram
    direction TB

    class FleetHub {
        <<sealed>>
        -AgentRegistry _registry
        -ILogger~FleetHub~ _logger
        +RegisterAgent(AgentRegistration registration) Task
        +SendHeartbeat(Heartbeat heartbeat) Task
        +ReportAuditEvent(AuditEvent auditEvent) Task
        +ReportCommandResult(CommandResult result) Task
        +OnDisconnectedAsync(Exception? exception) Task
    }

    class AgentRegistry {
        <<sealed>>
        -ConcurrentDictionary~string, AgentState~ _agents
        -ConcurrentDictionary~string, string~ _connections
        -ConcurrentBag~AuditEvent~ _auditLog
        +MapConnection(string agentId, string connectionId) void
        +RemoveConnection(string connectionId) void
        +GetConnectionId(string agentId) string?
        +RegisterOrUpdate(AgentRegistration reg, string connectionId) void
        +UpdateHeartbeat(Heartbeat hb) void
        +GetAllAgents() IReadOnlyList~AgentState~
        +GetAgent(string agentId) AgentState?
        +AppendAudit(AuditEvent evt) void
        +GetAuditLog(int take) IReadOnlyList~AuditEvent~
    }

    class AgentState {
        <<sealed>>
        +string AgentId
        +string Hostname
        +string OsDescription
        +bool IsOnline
        +DateTime LastSeen
        +double CpuPercent
        +double MemoryUsedMb
        +double DiskUsedPercent
        +List~ManagedApp~ ManagedApps
    }

    class `Hub~IAgentClient~` {
        <<abstract>>
        +Clients IHubCallerClients~IAgentClient~
        +Context HubCallerContext
    }

    class ICloudHub {
        <<interface>>
    }

    FleetHub --|> `Hub~IAgentClient~` : extends
    FleetHub ..|> ICloudHub : implements
    FleetHub --> AgentRegistry : uses
    AgentRegistry --> AgentState : manages
    AgentState --> ManagedApp : contains
```

---

## Class Diagram — Agent.Worker

The agent-side daemon: background worker, command handlers, and local app management.

```mermaid
classDiagram
    direction TB

    class AgentWorker {
        <<sealed>>
        -ILogger~AgentWorker~ _logger
        -LocalAppManager _appManager
        -IConfiguration _config
        #ExecuteAsync(CancellationToken stoppingToken) Task
        -RegisterSelf(HubConnection connection, string agentId) Task
        -BuildHeartbeat(string agentId) Heartbeat
        -ConnectWithRetryAsync(HubConnection connection, CancellationToken ct) Task
    }

    class BackgroundService {
        <<abstract>>
        #ExecuteAsync(CancellationToken stoppingToken)* Task
        +StartAsync(CancellationToken ct) Task
        +StopAsync(CancellationToken ct) Task
    }

    class CommandHandlerRegistration {
        <<static>>
        +RegisterHandlers(HubConnection connection, LocalAppManager appManager, string agentId, ILogger logger)$ void
    }

    class LocalAppManager {
        <<sealed>>
        -ILogger~LocalAppManager~ _logger
        -List~LocalApp~ _apps
        +GetManagedApps() List~ManagedApp~
        +RestartAppAsync(string appId) Task~bool~
        +ApplyConfigAsync(string appId, Dictionary~string, string~ settings) Task~bool~
        +UpdateAppAsync(string appId, string targetVersion, string artifactUrl) Task~bool~
    }

    class LocalApp {
        <<sealed>>
        +string AppId
        +string Name
        +string Version
        +AppStatus Status
        +Dictionary~string, string~ Config
    }

    AgentWorker --|> BackgroundService : extends
    AgentWorker --> LocalAppManager : uses
    AgentWorker --> CommandHandlerRegistration : calls
    CommandHandlerRegistration --> LocalAppManager : delegates to
    LocalAppManager --> LocalApp : manages
    LocalApp --> AppStatus : uses
```

---

## Full Class Relationship Diagram

A consolidated view of all types across the three projects and their relationships.

```mermaid
classDiagram
    direction LR

    namespace Shared_Contracts {
        class ICloudHub {
            <<interface>>
            +RegisterAgent(AgentRegistration) Task
            +SendHeartbeat(Heartbeat) Task
            +ReportAuditEvent(AuditEvent) Task
            +ReportCommandResult(CommandResult) Task
        }
        class IAgentClient {
            <<interface>>
            +PushConfig(PushConfigCommand) Task
            +RestartApp(RestartAppCommand) Task
            +UpdateApp(UpdateAppCommand) Task
            +RequestHeartbeat() Task
        }
        class AgentRegistration {
            <<record>>
        }
        class Heartbeat {
            <<record>>
        }
        class AuditEvent {
            <<record>>
        }
        class ManagedApp {
            <<record>>
        }
        class AppStatus {
            <<enumeration>>
        }
        class PushConfigCommand {
            <<record>>
        }
        class RestartAppCommand {
            <<record>>
        }
        class UpdateAppCommand {
            <<record>>
        }
        class CommandResult {
            <<record>>
        }
    }

    namespace CloudConsole_Api {
        class FleetHub {
            <<sealed>>
        }
        class AgentRegistry {
            <<sealed>>
        }
        class AgentState {
            <<sealed>>
        }
    }

    namespace Agent_Worker {
        class AgentWorker {
            <<sealed>>
        }
        class CommandHandlerRegistration {
            <<static>>
        }
        class LocalAppManager {
            <<sealed>>
        }
        class LocalApp {
            <<sealed>>
        }
    }

    FleetHub ..|> ICloudHub : implements
    FleetHub --> AgentRegistry : uses
    AgentRegistry --> AgentState : manages
    AgentRegistry --> AuditEvent : stores
    AgentState --> ManagedApp : contains

    AgentWorker --> LocalAppManager : uses
    AgentWorker --> CommandHandlerRegistration : calls
    CommandHandlerRegistration --> LocalAppManager : delegates to
    LocalAppManager --> LocalApp : manages
    LocalApp --> AppStatus : uses

    ICloudHub ..> AgentRegistration : param
    ICloudHub ..> Heartbeat : param
    ICloudHub ..> AuditEvent : param
    ICloudHub ..> CommandResult : param
    IAgentClient ..> PushConfigCommand : param
    IAgentClient ..> RestartAppCommand : param
    IAgentClient ..> UpdateAppCommand : param
```

---

## Sequence Diagram — Agent Registration & Heartbeat

Shows the startup flow when an agent connects to the cloud console.

```mermaid
sequenceDiagram
    participant Agent as AgentWorker
    participant Hub as HubConnection (SignalR)
    participant Fleet as FleetHub
    participant Registry as AgentRegistry

    Note over Agent: Service starts via systemd

    Agent->>Hub: Build connection to /hub/fleet
    Agent->>Hub: RegisterHandlers() — wire command callbacks

    loop Connect with retry (every 5s)
        Agent->>Hub: StartAsync()
        Hub-->>Fleet: WebSocket connected
    end

    Agent->>Hub: InvokeAsync("RegisterAgent", AgentRegistration)
    Hub->>Fleet: RegisterAgent(registration)
    Fleet->>Registry: RegisterOrUpdate(reg, connectionId)
    Fleet->>Registry: MapConnection(agentId, connectionId)
    Fleet->>Registry: AppendAudit("Agent registered")

    loop Every N seconds (default 10)
        Agent->>Agent: BuildHeartbeat() — gather metrics
        Agent->>Hub: InvokeAsync("SendHeartbeat", heartbeat)
        Hub->>Fleet: SendHeartbeat(heartbeat)
        Fleet->>Registry: UpdateHeartbeat(hb)
    end

    Note over Agent: On disconnect / reconnect
    Hub-->>Agent: Reconnected event
    Agent->>Hub: InvokeAsync("RegisterAgent", AgentRegistration)
```

---

## Sequence Diagram — Cloud Command Execution

Shows the flow when an operator sends a command (e.g. restart) through the REST API.

```mermaid
sequenceDiagram
    participant Browser as Browser / cURL
    participant API as REST Endpoint
    participant Registry as AgentRegistry
    participant Hub as FleetHub (IAgentClient proxy)
    participant Agent as AgentWorker
    participant Handler as CommandHandlerRegistration
    participant AppMgr as LocalAppManager

    Browser->>API: POST /api/agents/{id}/restart-app<br/>{ "appId": "radiology-ai" }
    API->>Registry: GetConnectionId(agentId)
    Registry-->>API: connectionId (or null → 404)

    API->>Hub: Clients.Client(connectionId).RestartApp(command)
    API->>Registry: AppendAudit("Restart requested")
    API-->>Browser: 202 Accepted

    Hub->>Agent: SignalR invokes RestartApp handler
    Agent->>Handler: On&lt;RestartAppCommand&gt; callback
    Handler->>AppMgr: RestartAppAsync(appId)
    AppMgr->>AppMgr: Status = Stopped → delay → Status = Running
    AppMgr-->>Handler: true (success)

    Handler->>Hub: InvokeAsync("ReportCommandResult", result)
    Hub->>Registry: (via FleetHub) AppendAudit(result)

    Handler->>Hub: InvokeAsync("ReportAuditEvent", auditEvent)
    Hub->>Registry: (via FleetHub) AppendAudit(auditEvent)
```

---

## Component Diagram

High-level architectural view of the system components and their communication channels.

```mermaid
graph TB
    subgraph Browser["Browser / Dashboard"]
        Dashboard["index.html<br/>(polls every 2s)"]
    end

    subgraph Cloud["CloudConsole.Api — ASP.NET Core"]
        REST["REST API<br/>/api/agents<br/>/api/audit<br/>/api/agents/{id}/*"]
        HubComp["FleetHub<br/>Hub&lt;IAgentClient&gt;"]
        RegistryComp["AgentRegistry<br/>(Singleton)"]
        AuditStore["Audit Log<br/>(ConcurrentBag)"]

        REST --> RegistryComp
        REST --> HubComp
        HubComp --> RegistryComp
        RegistryComp --> AuditStore
    end

    subgraph AgentN["Agent.Worker — Worker Service (× N)"]
        Worker["AgentWorker<br/>(BackgroundService)"]
        Handlers["CommandHandlerRegistration<br/>(static)"]
        AppMgr["LocalAppManager<br/>(Singleton)"]
        Apps["LocalApp instances<br/>(radiology-ai, dicom-gateway)"]

        Worker --> Handlers
        Worker --> AppMgr
        Handlers --> AppMgr
        AppMgr --> Apps
    end

    Dashboard -- "HTTP GET<br/>(polling)" --> REST
    Dashboard -- "HTTP POST<br/>(commands)" --> REST

    HubComp -- "SignalR WebSocket<br/>Cloud → Agent<br/>(PushConfig, RestartApp,<br/>UpdateApp, RequestHeartbeat)" --> Worker
    Worker -- "SignalR WebSocket<br/>Agent → Cloud<br/>(RegisterAgent, SendHeartbeat,<br/>ReportAuditEvent, ReportCommandResult)" --> HubComp

    style Cloud fill:#d5f5d5,stroke:#33aa33
    style AgentN fill:#fff3d5,stroke:#cc9933
    style Browser fill:#f0e0ff,stroke:#9944cc
```

---

## State Diagram — AppStatus

Lifecycle states a managed application can transition through.

```mermaid
stateDiagram-v2
    [*] --> Running : App starts

    Running --> Stopped : RestartAppAsync() called
    Stopped --> Running : Restart completes (after delay)

    Running --> Updating : UpdateAppAsync() called
    Updating --> Running : Update completes (after delay)

    Running --> Error : Unhandled failure
    Error --> Running : Manual recovery
    Error --> Stopped : Shutdown

    Stopped --> [*] : App removed
```

---

## State Diagram — Agent Lifecycle

Connection lifecycle of an agent from startup to graceful shutdown.

```mermaid
stateDiagram-v2
    [*] --> Connecting : Service starts

    Connecting --> Connected : StartAsync() succeeds
    Connecting --> Connecting : Retry after 5s

    Connected --> Registered : RegisterAgent() sent
    Registered --> Heartbeating : Enter heartbeat loop

    Heartbeating --> Heartbeating : SendHeartbeat every N sec

    Heartbeating --> Disconnected : Connection lost
    Disconnected --> Reconnecting : Auto-reconnect (0s, 2s, 5s, 10s)
    Reconnecting --> Connected : Reconnected event
    Reconnecting --> Disconnected : Reconnect failed

    Heartbeating --> ShuttingDown : CancellationToken triggered
    ShuttingDown --> [*] : StopAsync() — connection disposed
```
