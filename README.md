# Fleet Manager — Cloud Console + Agent Demo

A minimal but complete demonstration of the **Centralized Cloud Gateway Console + Local Client Agent** pattern built with the .NET 8 stack.

## Architecture

```
┌───────────────────────────────────────────────────────┐
│              CLOUD CONSOLE  (ASP.NET Core)            │
│                                                       │
│   ┌──────────────┐  ┌─────────────┐  ┌────────────┐   │
│   │  HTML        │  │  REST API   │  │  SignalR   │   │
│   │  Dashboard   │  │  /api/*     │  │  /hub/fleet│   │
│   │  (wwwroot)   │  │             │  │            │   │
│   └──────┬───────┘  └──────┬──────┘  └──────┬─────┘   │
│          │                 │                │         │
│          └─────────────────┼────────────────┘         │
│                            │                          │
│                     AgentRegistry                     │
│                   (in-memory store)                   │
└────────────────────────────┬──────────────────────────┘
                             │  SignalR (WebSocket)
                    ┌────────┴────────┐
                    │                 │
            ┌───────▼───────┐ ┌──────▼─────────┐
            │  Agent #1     │ │  Agent #2      │
            │  (Worker Svc) │ │  (Worker Svc)  │
            │               │ │                │
            │ LocalAppMgr   │ │ LocalAppMgr    │
            │ ┌───────────┐ │ │ ┌────────────┐ │
            │ │ App A     │ │ │ │ App A      │ │
            │ │ App B     │ │ │ │ App C      │ │
            │ └───────────┘ │ │ └────────────┘ │
            └───────────────┘ └────────────────┘
```

## Data Flow Summary

| Direction       | Transport | What                                    |
|----------------|-----------|-----------------------------------------|
| Agent → Cloud  | SignalR   | Registration, Heartbeat, Audit Events   |
| Cloud → Agent  | SignalR   | PushConfig, RestartApp, UpdateApp       |
| Browser → Cloud| HTTP REST | GET agents, GET audit, POST commands    |

## Project Structure

```
FleetManager/
├── FleetManager.sln
├── docs/
│   └── fleet-agent.service          # systemd unit file example
└── src/
    ├── Shared.Contracts/            # Shared DTOs & hub interfaces
    │   ├── Models/AgentModels.cs    #   Heartbeat, ManagedApp, AuditEvent
    │   ├── Messages/Commands.cs     #   PushConfig, RestartApp, UpdateApp
    │   └── IHubContracts.cs         #   ICloudHub, IAgentClient
    │
    ├── CloudConsole.Api/            # ASP.NET Core server
    │   ├── Hubs/FleetHub.cs         #   SignalR hub implementation
    │   ├── Services/AgentRegistry   #   In-memory agent state store
    │   ├── wwwroot/index.html       #   Dashboard UI
    │   └── Program.cs               #   Composition root + REST endpoints
    │
    └── Agent.Worker/                # .NET Worker Service (runs on Ubuntu)
        ├── Services/LocalAppManager #   Simulates managing local apps
        ├── Handlers/CommandHandler  #   Handles cloud→agent commands
        ├── AgentWorker.cs           #   Background service (heartbeat loop)
        └── Program.cs               #   Composition root
```

## Quick Start

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### 1. Start the Cloud Console

```bash
cd src/CloudConsole.Api
dotnet run
```

Open **http://localhost:5000** in your browser to see the dashboard.

### 2. Start an Agent (in a separate terminal)

```bash
cd src/Agent.Worker
dotnet run
```

The agent connects automatically and appears on the dashboard within seconds.

### 3. Start a Second Agent (optional)

```bash
cd src/Agent.Worker
dotnet run -- --Agent:Id=agent-pathology-02
```

### 4. Try Commands from the Dashboard

- Click **↻ Radiology AI** to restart an app
- Click **⚙ Config** to push a config change
- Watch the **Audit Log** update in real-time

### 5. Try Commands via cURL

```bash
# Restart an app
curl -X POST http://localhost:5000/api/agents/agent-radiology-01/restart-app \
  -H "Content-Type: application/json" \
  -d '{"appId":"radiology-ai"}'

# Push config
curl -X POST http://localhost:5000/api/agents/agent-radiology-01/push-config \
  -H "Content-Type: application/json" \
  -d '{"appId":"dicom-gateway","settings":{"LogLevel":"Debug","MaxRetries":"5"}}'

# Trigger an update
curl -X POST http://localhost:5000/api/agents/agent-radiology-01/update-app \
  -H "Content-Type: application/json" \
  -d '{"appId":"radiology-ai","targetVersion":"2.0.0","artifactUrl":"https://artifacts.example.com/radiology-ai-2.0.0.tar.gz"}'
```

## Deploy Agent as systemd Service

See `docs/fleet-agent.service` for a production-ready unit file.

```bash
# Publish self-contained binary
dotnet publish src/Agent.Worker -c Release -o /opt/fleet-agent

# Install and start
sudo cp docs/fleet-agent.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now fleet-agent

# Check logs
journalctl -u fleet-agent -f
```

## Production Considerations

This demo intentionally keeps things simple. For production, consider:

| Area              | Demo                  | Production                                  |
|-------------------|-----------------------|---------------------------------------------|
| Agent Registry    | In-memory dictionary  | PostgreSQL + EF Core                        |
| Authentication    | None                  | mTLS client certs or API key + JWT          |
| Dashboard         | Polling HTML          | Blazor/React with SignalR push to browser   |
| Artifact Storage  | Fake URL              | Azure Blob / S3 / MinIO                     |
| Config Store      | Simulated             | Database-backed with versioning             |
| Observability     | Console logs          | OpenTelemetry → Prometheus/Grafana          |
| Update Rollback   | Not implemented       | Backup → swap → health check → rollback     |
| Multi-tenancy     | Single tenant         | Tenant isolation in hub groups              |
