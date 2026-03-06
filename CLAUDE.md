# FleetManager

## What This Project Is

`FleetManager` is an **educational** repo for learning a **centralized cloud gateway console + local client agent** system for remote monitoring, auditing, configuring, and updating .NET applications installed on Ubuntu servers. It follows the **fleet management / control-plane + data-plane** pattern.

## Tech Stack

- **.NET 8** (C# 12, top-level statements, primary constructors, collection expressions)
- **ASP.NET Core** — Cloud Console web API
- **SignalR** — Bidirectional real-time communication (cloud ↔ agent)
- **Worker Service** (`Microsoft.Extensions.Hosting`) — Agent daemon on Ubuntu
- **systemd integration** (`Microsoft.Extensions.Hosting.Systemd`) — Linux service support
- No database yet — in-memory `ConcurrentDictionary` (production: PostgreSQL + EF Core)

## Solution Structure

```
FleetManager.sln
├── src/Shared.Contracts/       # Shared DTOs, hub interfaces (no dependencies)
│   ├── Models/AgentModels.cs   #   Heartbeat, ManagedApp, AuditEvent, AppStatus
│   ├── Messages/Commands.cs    #   PushConfigCommand, RestartAppCommand, UpdateAppCommand
│   └── IHubContracts.cs        #   ICloudHub (agent→cloud), IAgentClient (cloud→agent)
│
├── src/CloudConsole.Api/       # ASP.NET Core server (depends on Shared.Contracts)
│   ├── Hubs/FleetHub.cs        #   Strongly-typed SignalR hub: Hub<IAgentClient>
│   ├── Services/AgentRegistry  #   Thread-safe in-memory agent state + audit log
│   ├── wwwroot/index.html      #   Static HTML dashboard (polls REST API)
│   └── Program.cs              #   Composition root, REST endpoints, hub mapping
│
├── src/Agent.Worker/           # .NET Worker Service (depends on Shared.Contracts)
│   ├── AgentWorker.cs          #   BackgroundService: connect → register → heartbeat loop
│   ├── Handlers/CommandHandlerRegistration.cs  # Wires SignalR .On<T>() for cloud commands
│   ├── Services/LocalAppManager.cs             # Simulates local app lifecycle
│   └── Program.cs              #   Composition root with AddSystemd()
│
└── docs/fleet-agent.service    # Example systemd unit file
```

## Dependency Rule

```
CloudConsole.Api ──► Shared.Contracts ◄── Agent.Worker
```

Both executable projects depend on `Shared.Contracts`. They never reference each other. All shared types (DTOs, hub interfaces, enums) live in `Shared.Contracts`.

## Key Communication Pattern

SignalR is the single transport for all cloud ↔ agent communication:

- **Agent → Cloud** (agent calls hub methods): `RegisterAgent`, `SendHeartbeat`, `ReportAuditEvent`, `ReportCommandResult`
- **Cloud → Agent** (hub invokes client proxy): `PushConfig`, `RestartApp`, `UpdateApp`, `RequestHeartbeat`
- **Browser → Cloud**: REST API (`GET /api/agents`, `GET /api/audit`, `POST /api/agents/{id}/restart-app`, etc.)

The strongly-typed hub (`Hub<IAgentClient>`) and interface pair (`ICloudHub` / `IAgentClient`) ensure compile-time safety on both sides.

## Build and Run

```bash
# Build entire solution
dotnet build

# Run cloud console (Terminal 1)
dotnet run --project src/CloudConsole.Api
# Listens on http://localhost:5000

# Run agent (Terminal 2)
dotnet run --project src/Agent.Worker
# Connects to cloud via SignalR, sends heartbeats every 10s

# Run second agent with different ID
dotnet run --project src/Agent.Worker -- --Agent:Id=agent-pathology-02
```

Dashboard at http://localhost:5000. Agent config in `src/Agent.Worker/appsettings.json`.

## Coding Conventions

- **Records for DTOs** — All shared models are `sealed record` types (immutable, value equality).
- **Sealed by default** — Concrete classes are `sealed` unless designed for inheritance.
- **Primary constructors** — Used on records and simple classes (C# 12).
- **Collection expressions** — Use `[]` syntax for list/array initialization.
- **Minimal API** — REST endpoints defined inline in `Program.cs` via `MapGet`/`MapPost`.
- **No MediatR** — Project is simple enough for direct service calls.
- **ConcurrentDictionary / ConcurrentBag** — Thread safety for in-memory stores (SignalR hub calls are concurrent).
- **XML doc comments** — On all public types and hub methods explaining direction and purpose.
- **Namespace = folder path** — `CloudConsole.Api.Hubs`, `Agent.Worker.Services`, etc.

## Testing Commands via cURL

```bash
# Restart an app on a specific agent
curl -X POST http://localhost:5000/api/agents/agent-radiology-01/restart-app \
  -H "Content-Type: application/json" \
  -d '{"appId":"radiology-ai"}'

# Push config to an app
curl -X POST http://localhost:5000/api/agents/agent-radiology-01/push-config \
  -H "Content-Type: application/json" \
  -d '{"appId":"dicom-gateway","settings":{"LogLevel":"Debug"}}'

# Trigger app update
curl -X POST http://localhost:5000/api/agents/agent-radiology-01/update-app \
  -H "Content-Type: application/json" \
  -d '{"appId":"radiology-ai","targetVersion":"2.0.0","artifactUrl":"https://artifacts.example.com/v2.tar.gz"}'
```

## Important Design Notes

- `LocalAppManager` is a **simulation**. In production, it would call `systemctl` via `Process.Start`, write real `appsettings.json` files, and download artifacts from blob storage.
- `AgentRegistry` is **in-memory only**. Data is lost on restart. Replace with EF Core + PostgreSQL for persistence.
- **No authentication** in this demo. Production requires mTLS (client certs per agent) or API key + JWT.
- CPU/disk metrics in `AgentWorker.BuildHeartbeat()` are **simulated** with random values. Real implementation reads `/proc/stat` and `/proc/diskstats` on Linux.
- The HTML dashboard **polls** every 2 seconds. A production dashboard should use a second SignalR connection to receive push updates.

## Where to Extend

| Feature                  | Where to Add                                         |
|--------------------------|------------------------------------------------------|
| Database persistence     | New `Infrastructure/` project with EF Core DbContext  |
| Agent authentication     | Middleware in `CloudConsole.Api` + cert config in Agent |
| Real system metrics      | `Agent.Worker/Services/SystemMetricsCollector.cs`     |
| Blazor dashboard         | New `CloudConsole.Dashboard/` project                 |
| Update rollback logic    | `LocalAppManager.UpdateAppAsync()` — backup/restore  |
| OpenTelemetry            | `Program.cs` in both projects — `AddOpenTelemetry()` |
| Health checks            | `MapHealthChecks()` in cloud, custom check in agent   |
