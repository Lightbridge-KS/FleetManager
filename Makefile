# ──────────────────────────────────────────────────────────────
# FleetManager — Dev Makefile
# ──────────────────────────────────────────────────────────────
# Usage:
#   make build          — build entire solution
#   make cloud          — run Cloud Console (Terminal 1)
#   make agent          — run Agent Worker  (Terminal 2)
#   make cli-*          — run fleet-ctl commands (Terminal 3)
#
# Tip: Use separate terminal tabs for cloud, agent, and CLI.
# ──────────────────────────────────────────────────────────────

CLOUD_URL    := http://localhost:6100
CLI_PROJECT  := src/Fleet.Cli
CLI          := dotnet run --project $(CLI_PROJECT) --
AGENT_ID     := agent-radiology-01
APP_ID       := radiology-ai

# ── Build & Test ──────────────────────────────────────────────

.PHONY: build test restore clean

build:
	dotnet build

test:
	dotnet test --verbosity normal

restore:
	dotnet restore

clean:
	dotnet clean

# ── Run Services ──────────────────────────────────────────────

.PHONY: cloud agent agent2

cloud:
	dotnet run --project src/CloudConsole.Api

agent:
	dotnet run --project src/Agent.Worker

agent2:
	dotnet run --project src/Agent.Worker -- --Agent:Id=agent-pathology-02

# ── CLI: Login ────────────────────────────────────────────────

.PHONY: cli-login cli-help

cli-login:
	$(CLI) login --url $(CLOUD_URL) --api-key ""

cli-help:
	$(CLI) --help

# ── CLI: Agents ───────────────────────────────────────────────

.PHONY: cli-agents cli-agents-json cli-agents-online cli-agent-status

cli-agents:
	$(CLI) agents list

cli-agents-json:
	$(CLI) agents list --json

cli-agents-online:
	$(CLI) agents list --online-only

cli-agent-status:
	$(CLI) agents status $(AGENT_ID)

# ── CLI: Apps ─────────────────────────────────────────────────

.PHONY: cli-app-restart cli-app-config cli-app-update

cli-app-restart:
	$(CLI) apps restart $(AGENT_ID) $(APP_ID)

cli-app-config:
	$(CLI) apps config $(AGENT_ID) dicom-gateway --set LogLevel=Debug

cli-app-update:
	$(CLI) apps update $(AGENT_ID) $(APP_ID) \
		--version 2.0.0 \
		--artifact https://artifacts.example.com/v2.tar.gz

# ── CLI: Audit ────────────────────────────────────────────────

.PHONY: cli-audit cli-audit-json

cli-audit:
	$(CLI) audit --last 5

cli-audit-json:
	$(CLI) audit --last 5 --json
