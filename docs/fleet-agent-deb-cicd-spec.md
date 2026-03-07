# Fleet Agent: `.deb` Packaging, CLI Tool & CI/CD Spec Plan

## Objective

Set up the FleetManager `Agent.Worker` project so that every tagged release automatically builds a `.deb` package and publishes it to GitHub Releases. Additionally, provide a `fleet-ctl` CLI tool that admins use to interact with the cloud console from their terminal. Admins install the agent on Ubuntu servers by downloading the `.deb` from the release page, and install the CLI tool on any machine (macOS, Linux, or WSL).

## Target Outcome

```
Developer pushes tag          GitHub Actions              Ubuntu Server
─────────────────────         ──────────────────          ──────────────────
git tag v1.2.0          ───►  test → publish →            curl -LO ...deb
git push --tags               build .deb →                sudo dpkg -i fleet-agent_1.2.0_amd64.deb
                              build CLI →                 ✓ Agent running as systemd service
                              upload to Release

Admin workstation (macOS/Linux)
──────────────────────────────
fleet-ctl login --url https://console.example.com
fleet-ctl agents list
fleet-ctl agents status agent-radiology-01
fleet-ctl apps restart agent-radiology-01 radiology-ai
fleet-ctl audit --last 20
```

---

## 1. Repository Structure (Changes Only)

Add these files/directories to the existing FleetManager repo. Do not modify existing source code unless explicitly stated.

```
FleetManager/
├── (existing src/, docs/, FleetManager.sln, README.md, CLAUDE.md)
│
├── src/
│   ├── (existing Shared.Contracts/, CloudConsole.Api/, Agent.Worker/)
│   │
│   └── Fleet.Cli/                      # NEW: CLI tool project
│       ├── Fleet.Cli.csproj
│       ├── Program.cs                  #   Entry point, root command
│       ├── Commands/                   #   Command implementations
│       │   ├── LoginCommand.cs
│       │   ├── AgentsCommand.cs        #   agents list, agents status <id>
│       │   ├── AppsCommand.cs          #   apps restart, apps config, apps update
│       │   └── AuditCommand.cs         #   audit log viewer
│       ├── Services/
│       │   ├── CloudApiClient.cs       #   HTTP client for Cloud Console REST API
│       │   └── CliConfigManager.cs     #   Reads/writes ~/.config/fleet-ctl/
│       └── Models/
│           └── CliConfig.cs            #   Config file model
│
├── packaging/                          # NEW: .deb packaging assets
│   ├── build-deb.sh                    #   Shell script that assembles the agent .deb
│   ├── DEBIAN/
│   │   ├── control.template
│   │   ├── conffiles
│   │   ├── postinst
│   │   ├── prerm
│   │   └── postrm
│   └── etc/
│       └── fleet-agent/
│           └── agent.env.default
│
├── .github/
│   └── workflows/
│       └── release.yml                 #   GitHub Actions workflow (agent + CLI)
│
└── scripts/
    └── install-from-github.sh          #   Convenience installer for agent .deb
```

Update `FleetManager.sln` to include `Fleet.Cli.csproj`.

---

## 2. CLI Tool (`src/Fleet.Cli/`)

### 2.1 Overview

`fleet-ctl` is a cross-platform command-line tool for admins to interact with the Cloud Console REST API. It follows standard CLI conventions: per-user config in `~/.config/`, human-readable output by default, JSON output with `--json` flag.

```
fleet-ctl (CLI, per-user)                fleet-agent (daemon, system-wide)
──────────────────────────               ─────────────────────────────────
Config: ~/.config/fleet-ctl/             Config: /etc/fleet-agent/
Runs as: the human admin                 Runs as: fleet-agent system user
Lifecycle: invoked, runs, exits          Lifecycle: always running (systemd)
Talks to: Cloud Console REST API         Talks to: Cloud Console SignalR hub
Installed: dotnet tool / download        Installed: .deb package
```

### 2.2 Project File (`Fleet.Cli.csproj`)

```
Target framework: net8.0
Output type: Exe
Assembly name: fleet-ctl
NuGet dependencies:
  - System.CommandLine (2.0.0-beta4.*)   ← Microsoft's official CLI parsing library
```

Project reference: `Shared.Contracts` (to reuse DTOs like `ManagedApp`, `AuditEvent`, `AppStatus`).

Do NOT reference `CloudConsole.Api` or `Agent.Worker`.

### 2.3 Config Location & Format

The CLI stores its per-user configuration at `~/.config/fleet-ctl/config.json`. This follows the XDG Base Directory Specification (use `Environment.GetFolderPath(SpecialFolder.ApplicationData)` on non-Linux or `XDG_CONFIG_HOME` / `~/.config` on Linux).

```json
{
  "consoleUrl": "https://console.example.com",
  "apiKey": "sk-...",
  "defaultOutput": "table"
}
```

The `CliConfigManager` service handles:
- First-run: create `~/.config/fleet-ctl/` directory and empty config
- Read config: deserialize from JSON
- Write config: serialize to JSON, set file mode `600` on Linux/macOS (`chmod` via `File.SetUnixFileMode`)
- Validate: check that `consoleUrl` is set before any API command

### 2.4 Command Tree

```
fleet-ctl
│
├── login                                  Set console URL and API key
│   Options:
│     --url <url>          Console URL (required)
│     --api-key <key>      API key (optional, prompts interactively if omitted)
│
├── agents                                 Agent management
│   ├── list                               List all agents
│   │   Options:
│   │     --json           Output as JSON
│   │     --online-only    Show only online agents
│   │
│   └── status <agentId>                   Show detailed agent status
│       Options:
│         --json           Output as JSON
│
├── apps                                   App management (commands sent via cloud → agent)
│   ├── restart <agentId> <appId>          Restart a managed app
│   │
│   ├── config <agentId> <appId>           Push config to a managed app
│   │   Options:
│   │     --set <key=value>   One or more key=value pairs (repeatable)
│   │                          e.g., --set LogLevel=Debug --set MaxRetries=5
│   │
│   └── update <agentId> <appId>           Update a managed app
│       Options:
│         --version <ver>     Target version (required)
│         --artifact <url>    Artifact download URL (required)
│
└── audit                                  View audit log
    Options:
      --last <n>           Number of recent entries (default: 20)
      --agent <agentId>    Filter by agent
      --json               Output as JSON
```

### 2.5 Command Implementations

Each command class wires up a `System.CommandLine.Command` with its arguments, options, and handler.

#### `LoginCommand`

1. Accept `--url` and `--api-key` options.
2. If `--api-key` is omitted, prompt interactively: read from console with masking (use `Console.ReadKey` loop, print `*` per char).
3. Validate URL format.
4. Test connectivity: `GET {url}/api/agents` — if it returns 200, save config. If not, show error and do NOT save.
5. Write to `~/.config/fleet-ctl/config.json`.
6. Print: `Logged in to {url} successfully. Config saved to ~/.config/fleet-ctl/config.json`

#### `AgentsCommand` — `list` subcommand

1. `GET /api/agents` via `CloudApiClient`.
2. Default (table) output:

```
AGENT ID              HOSTNAME              STATUS   CPU     MEM (MB)  APPS
agent-radiology-01    radiology-srv-01      online   23.4%   128       2
agent-pathology-02    pathology-srv-02      offline  -       -         3
```

3. With `--json`: print raw JSON array from API response.
4. With `--online-only`: filter client-side where `isOnline == true`.

#### `AgentsCommand` — `status <agentId>` subcommand

1. `GET /api/agents/{agentId}` via `CloudApiClient`.
2. If 404: print `Agent '{agentId}' not found.` and exit code 1.
3. Default output:

```
Agent:    agent-radiology-01
Hostname: radiology-srv-01
OS:       Ubuntu 24.04 LTS
Status:   online
Last Seen: 2026-03-07 10:30:15 UTC

Metrics:
  CPU:   23.4%
  MEM:   128.3 MB
  Disk:  52.1%

Managed Apps:
  NAME                 VERSION    STATUS
  radiology-ai         1.2.0      Running
  dicom-gateway        3.0.1      Running
```

#### `AppsCommand` — `restart` subcommand

1. `POST /api/agents/{agentId}/restart-app` with body `{"appId": "<appId>"}`.
2. On `202 Accepted`: print `Restart command sent to {appId} on {agentId}.`
3. On `404`: print `Agent '{agentId}' not connected.`

#### `AppsCommand` — `config` subcommand

1. Parse `--set key=value` pairs into a `Dictionary<string, string>`.
2. If no `--set` provided: print error and usage help.
3. `POST /api/agents/{agentId}/push-config` with body `{"appId": "<appId>", "settings": {...}}`.
4. On `202 Accepted`: print `Config pushed to {appId} on {agentId}: {key1}, {key2}, ...`

#### `AppsCommand` — `update` subcommand

1. Both `--version` and `--artifact` are required.
2. `POST /api/agents/{agentId}/update-app` with body `{"appId":"...","targetVersion":"...","artifactUrl":"..."}`.
3. On `202 Accepted`: print `Update command sent: {appId} → v{version} on {agentId}.`

#### `AuditCommand`

1. `GET /api/audit` via `CloudApiClient`.
2. Client-side: take last N entries, optionally filter by agentId.
3. Default output:

```
TIMESTAMP                AGENT                CATEGORY       MESSAGE
2026-03-07 10:30:15      agent-radiology-01   AppLifecycle   App radiology-ai restarted
2026-03-07 10:28:03      agent-radiology-01   Config         Config pushed to dicom-gateway: LogLevel
2026-03-07 10:25:00      agent-pathology-02   Lifecycle      Agent registered
```

4. With `--json`: print raw JSON array.

### 2.6 `CloudApiClient` Service

A thin wrapper around `HttpClient` that:
- Reads `consoleUrl` and `apiKey` from `CliConfigManager`
- Sets `Authorization: Bearer {apiKey}` header on all requests (prepared for future auth; currently the demo API has no auth, so include the header only if apiKey is non-empty)
- Sets `Accept: application/json`
- Base URL = `consoleUrl` from config
- Methods mirror the REST API:
  - `GetAgentsAsync()` → `GET /api/agents`
  - `GetAgentAsync(string agentId)` → `GET /api/agents/{agentId}`
  - `GetAuditLogAsync()` → `GET /api/audit`
  - `RestartAppAsync(string agentId, RestartAppCommand cmd)` → `POST /api/agents/{agentId}/restart-app`
  - `PushConfigAsync(string agentId, PushConfigCommand cmd)` → `POST /api/agents/{agentId}/push-config`
  - `UpdateAppAsync(string agentId, UpdateAppCommand cmd)` → `POST /api/agents/{agentId}/update-app`
- On non-success status codes: throw a descriptive exception or return a result type that the command handler can format as a user-friendly error message.

### 2.7 `Program.cs` Entry Point

Use `System.CommandLine` to build the command tree:

```
var rootCommand = new RootCommand("fleet-ctl — Fleet Manager CLI");
rootCommand.AddCommand(BuildLoginCommand(...));
rootCommand.AddCommand(BuildAgentsCommand(...));
rootCommand.AddCommand(BuildAppsCommand(...));
rootCommand.AddCommand(BuildAuditCommand(...));
return await rootCommand.InvokeAsync(args);
```

Register `CliConfigManager` and `CloudApiClient` as shared instances passed to command builders. Do NOT use `Microsoft.Extensions.DependencyInjection` — this is a CLI tool, keep it simple with manual construction in `Program.cs`.

---

## 3. Packaging Assets (`packaging/`)

### 3.1 `DEBIAN/control.template`

Standard Debian control file with `{{VERSION}}` placeholder that `build-deb.sh` substitutes at build time.

Fields to include:

| Field          | Value                                                          |
|----------------|----------------------------------------------------------------|
| Package        | `fleet-agent`                                                  |
| Version        | `{{VERSION}}` (substituted by build script, e.g., `1.2.0`)    |
| Section        | `admin`                                                        |
| Priority       | `optional`                                                     |
| Architecture   | `amd64`                                                        |
| Depends        | `libicu74 | libicu72 | libicu70, libssl3 | libssl1.1`         |
| Maintainer     | `FleetManager Team <infra@example.com>`                        |
| Homepage       | `https://github.com/Lightbridge-KS/FleetManager`                    |
| Description    | Short + long description of the fleet agent                    |

### 3.2 `DEBIAN/conffiles`

```
/etc/fleet-agent/agent.env
```

This tells dpkg to preserve the admin's config on upgrade (prompt or keep existing).

### 3.3 `DEBIAN/postinst`

Runs after package files are placed on disk. Must be `#!/bin/bash`, `set -e`, and handle both fresh install and upgrade.

Actions:
1. Create `fleet-agent` system user if it doesn't exist (`useradd --system --no-create-home --shell /usr/sbin/nologin fleet-agent`)
2. Create `/var/lib/fleet-agent` if it doesn't exist
3. `chown -R fleet-agent:fleet-agent /opt/fleet-agent`
4. `chown -R fleet-agent:fleet-agent /var/lib/fleet-agent`
5. `chown fleet-agent:fleet-agent /etc/fleet-agent/agent.env`
6. `chmod 600 /etc/fleet-agent/agent.env`
7. `chmod +x /opt/fleet-agent/Agent.Worker`
8. `systemctl daemon-reload`
9. `systemctl enable fleet-agent`
10. If the service is already active, `systemctl restart fleet-agent`. Otherwise, `systemctl start fleet-agent`.

### 3.4 `DEBIAN/prerm`

Runs before package files are removed. Actions:
1. `systemctl stop fleet-agent` (if active)
2. `systemctl disable fleet-agent` (ignore errors)

### 3.5 `DEBIAN/postrm`

Runs after package files are removed. Actions:
1. On `purge` only (`if [ "$1" = "purge" ]`): remove `fleet-agent` user, remove `/var/lib/fleet-agent`, remove `/etc/fleet-agent`
2. Always: `systemctl daemon-reload`

### 3.6 `etc/fleet-agent/agent.env.default`

Default environment file shipped in the package. Contents:

```bash
DOTNET_ENVIRONMENT=Production
CloudConsole__HubUrl=https://console.example.com/hub/fleet
Agent__Id=agent-CHANGEME
Agent__HeartbeatIntervalSec=30
```

The `build-deb.sh` script copies this to `etc/fleet-agent/agent.env` inside the package. On first install, it becomes `/etc/fleet-agent/agent.env`. On upgrade, dpkg preserves the admin's existing version because it's listed in `conffiles`.

---

## 4. Build Script (`packaging/build-deb.sh`)

A self-contained bash script that produces the `.deb` file. Must work both locally and in GitHub Actions.

### Interface

```bash
# Usage:
./packaging/build-deb.sh <version>

# Example:
./packaging/build-deb.sh 1.2.0

# Output:
./dist/fleet-agent_1.2.0_amd64.deb
```

### Steps (In Order)

```
1. Parse version argument. Validate it matches semver pattern (N.N.N).

2. Run dotnet publish:
     dotnet publish src/Agent.Worker -c Release \
       --self-contained true \
       -r linux-x64 \
       -p:PublishSingleFile=true \
       -p:IncludeNativeLibrariesForSelfExtract=true \
       -o ./tmp-publish

3. Create staging directory mirroring target filesystem:
     ./tmp-deb/fleet-agent_<version>_amd64/
       ├── DEBIAN/
       ├── opt/fleet-agent/
       ├── etc/fleet-agent/
       ├── etc/systemd/system/
       └── var/lib/fleet-agent/   (empty, just the directory)

4. Copy files into staging:
     - Published binary + appsettings.json  →  opt/fleet-agent/
     - docs/fleet-agent.service             →  etc/systemd/system/
     - packaging/etc/fleet-agent/agent.env.default → etc/fleet-agent/agent.env
     - packaging/DEBIAN/*                   →  DEBIAN/

5. Substitute {{VERSION}} in DEBIAN/control with actual version.

6. Set permissions:
     - DEBIAN/postinst, prerm, postrm  →  755
     - DEBIAN/control, conffiles       →  644

7. Build: dpkg-deb --build --root-owner-group <staging-dir> ./dist/

8. Print package info: dpkg-deb --info ./dist/fleet-agent_<version>_amd64.deb

9. Clean up tmp-publish and tmp-deb directories.
```

### Important Details

- Use `set -euo pipefail` at the top.
- Use `--root-owner-group` flag on `dpkg-deb` so file ownership is root:root in the package (actual ownership is set by `postinst` at install time).
- The script should be runnable from the repo root directory.
- Output goes to `./dist/` directory (create if not exists).

---

## 5. GitHub Actions Workflow (`.github/workflows/release.yml`)

### Trigger

```yaml
on:
  push:
    tags:
      - 'v*.*.*'    # e.g., v1.0.0, v1.2.3
```

### Jobs

#### Job 1: `test`

Runs on `ubuntu-latest`. Steps:
1. Checkout code
2. Setup .NET 8 SDK (`actions/setup-dotnet@v4` with `dotnet-version: '8.0.x'`)
3. `dotnet restore`
4. `dotnet build --no-restore`
5. `dotnet test --no-build --verbosity normal` (even if no tests yet, keep the step for future)

#### Job 2: `build-deb`

Depends on `test` (`needs: test`). Runs on `ubuntu-latest`. Steps:

1. Checkout code
2. Setup .NET 8 SDK
3. Extract version from git tag:
   ```yaml
   - name: Extract version from tag
     id: version
     run: echo "VERSION=${GITHUB_REF_NAME#v}" >> $GITHUB_OUTPUT
   ```
   This strips the `v` prefix: `v1.2.0` → `1.2.0`
4. Run the build script:
   ```yaml
   - name: Build .deb package
     run: chmod +x ./packaging/build-deb.sh && ./packaging/build-deb.sh ${{ steps.version.outputs.VERSION }}
   ```
5. Upload the `.deb` as a workflow artifact:
   ```yaml
   - name: Upload .deb artifact
     uses: actions/upload-artifact@v4
     with:
       name: fleet-agent-deb
       path: ./dist/fleet-agent_*.deb
   ```

#### Job 3: `build-cli`

Depends on `test` (`needs: test`). Runs on `ubuntu-latest`. Builds the CLI as self-contained binaries for three platforms.

**Strategy matrix:**

| Runtime ID      | OS Label         | Artifact Name          |
|-----------------|------------------|------------------------|
| `linux-x64`     | `linux-x64`      | `fleet-ctl-linux-x64`  |
| `osx-x64`       | `macos-x64`      | `fleet-ctl-macos-x64`  |
| `osx-arm64`     | `macos-arm64`    | `fleet-ctl-macos-arm64`|

Steps (per matrix entry):

1. Checkout code
2. Setup .NET 8 SDK
3. Publish the CLI:
   ```bash
   dotnet publish src/Fleet.Cli -c Release \
     --self-contained true \
     -r ${{ matrix.runtime }} \
     -p:PublishSingleFile=true \
     -p:IncludeNativeLibrariesForSelfExtract=true \
     -o ./dist/cli/${{ matrix.os-label }}
   ```
4. Tar the output:
   ```bash
   tar -czf fleet-ctl-${{ matrix.os-label }}.tar.gz -C ./dist/cli/${{ matrix.os-label }} .
   ```
5. Upload as workflow artifact:
   ```yaml
   - name: Upload CLI artifact
     uses: actions/upload-artifact@v4
     with:
       name: fleet-ctl-${{ matrix.os-label }}
       path: ./fleet-ctl-${{ matrix.os-label }}.tar.gz
   ```

#### Job 4: `release`

Depends on both `build-deb` AND `build-cli` (`needs: [build-deb, build-cli]`). Runs on `ubuntu-latest`. Steps:

1. Download ALL artifacts from previous jobs (`actions/download-artifact@v4` with `merge-multiple: true` or multiple download steps)
2. Create GitHub Release and upload all assets:
   ```yaml
   - name: Create GitHub Release
     uses: softprops/action-gh-release@v2
     with:
       files: |
         ./fleet-agent-deb/fleet-agent_*.deb
         ./fleet-ctl-linux-x64/fleet-ctl-linux-x64.tar.gz
         ./fleet-ctl-macos-x64/fleet-ctl-macos-x64.tar.gz
         ./fleet-ctl-macos-arm64/fleet-ctl-macos-arm64.tar.gz
       generate_release_notes: true
       draft: false
       prerelease: false
     env:
       GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
   ```

### Required Permissions

```yaml
permissions:
  contents: write    # needed to create releases
```

### Full Workflow Pipeline Visualization

```
push tag v1.2.0
    │
    ▼
┌──────────┐     ┌─────────────┐
│   test   │────►│  build-deb  │────┐
│          │     │             │    │
│ restore  │     │ publish     │    │
│ build    │     │ build .deb  │    │     ┌──────────────┐
│ test     │     │ upload      │    ├────►│   release    │
│          │     └─────────────┘    │     │              │
│          │                        │     │ download all │
│          │     ┌─────────────┐    │     │ create GH    │
│          │────►│  build-cli  │────┘     │ release      │
│          │     │             │          │ attach:      │
└──────────┘     │ matrix:     │          │  .deb        │
                 │  linux-x64  │          │  cli tarballs│
                 │  macos-x64  │          └──────────────┘
                 │  macos-arm64│
                 └─────────────┘
```

---

## 6. Convenience Installer Script (`scripts/install-from-github.sh`)

A single-command installer for end users. Downloads the latest `.deb` from GitHub Releases and installs it.

### Usage

```bash
curl -fsSL https://raw.githubusercontent.com/Lightbridge-KS/FleetManager/main/scripts/install-from-github.sh | sudo bash
```

Or with a specific version:

```bash
curl -fsSL https://raw.githubusercontent.com/Lightbridge-KS/FleetManager/main/scripts/install-from-github.sh | sudo bash -s -- 1.2.0
```

### Script Logic

```
1. Accept optional version argument. If not provided, query GitHub API for latest release tag:
     curl -s https://api.github.com/repos/Lightbridge-KS/FleetManager/releases/latest
     Extract tag_name, strip 'v' prefix.

2. Construct download URL:
     https://github.com/Lightbridge-KS/FleetManager/releases/download/v${VERSION}/fleet-agent_${VERSION}_amd64.deb

3. Download .deb to /tmp/fleet-agent.deb

4. Verify file exists and is non-empty.

5. Install: dpkg -i /tmp/fleet-agent.deb

6. Clean up: rm /tmp/fleet-agent.deb

7. Print status: systemctl status fleet-agent --no-pager

8. Remind admin to edit /etc/fleet-agent/agent.env with actual values.
```

---

## 7. systemd Service File Update

Update the existing `docs/fleet-agent.service` to be the canonical version used by the `.deb` package. It should include all security hardening directives.

Key properties:
- `Type=notify` (works with `AddSystemd()`)
- `EnvironmentFile=/etc/fleet-agent/agent.env`
- Remove any hardcoded `Environment=` lines (those now live in `agent.env`)
- Keep all security hardening: `ProtectSystem=strict`, `ProtectHome=true`, `PrivateTmp=true`, `NoNewPrivileges=true`, `ReadWritePaths=/var/lib/fleet-agent`, `ReadOnlyPaths=/opt/fleet-agent`
- `Restart=always`, `RestartSec=10`
- `User=fleet-agent`, `Group=fleet-agent`

---

## 8. File Placement Summary

### After `dpkg -i fleet-agent_1.2.0_amd64.deb` (Ubuntu server):

```
/opt/fleet-agent/                    # From package: application binaries
│   ├── Agent.Worker                 #   Self-contained single-file executable
│   └── appsettings.json             #   Default app config
│
/etc/fleet-agent/                    # From package: admin config (conffile)
│   └── agent.env                    #   Secrets + overrides (mode 600)
│
/etc/systemd/system/                 # From package: service definition
│   └── fleet-agent.service          #   systemd unit file
│
/var/lib/fleet-agent/                # From package: empty dir, service-writable
│
systemd:
  fleet-agent.service  →  enabled + active (started by postinst)
  User: fleet-agent (system user, no login, no home)
```

### After extracting `fleet-ctl-macos-arm64.tar.gz` (admin workstation):

```
~/bin/fleet-ctl                      # The executable (user puts it on $PATH)

~/.config/fleet-ctl/                 # Created on first `fleet-ctl login`
│   └── config.json                  #   Console URL + API key (mode 600)
```

### Side-by-Side Comparison

```
                     fleet-agent (daemon)       fleet-ctl (CLI)
                     ─────────────────────      ──────────────────
Installed by         dpkg / apt                 Manual extract / brew
Runs as              fleet-agent system user    The human admin
Lifecycle            Always running (systemd)   Runs on demand, exits
Config path          /etc/fleet-agent/          ~/.config/fleet-ctl/
Data path            /var/lib/fleet-agent/      (none — stateless)
Talks to             Cloud Console (SignalR)    Cloud Console (REST API)
Installed on         Ubuntu servers             Admin's Mac / workstation
```

---

## 9. End-User Workflow

### Agent: First Install

```bash
# Download and install
curl -LO https://github.com/Lightbridge-KS/FleetManager/releases/download/v1.2.0/fleet-agent_1.2.0_amd64.deb
sudo dpkg -i fleet-agent_1.2.0_amd64.deb

# Configure (required: set actual Hub URL and Agent ID)
sudo nano /etc/fleet-agent/agent.env

# Restart to pick up config
sudo systemctl restart fleet-agent

# Verify
sudo systemctl status fleet-agent
journalctl -u fleet-agent -f
```

### Agent: Upgrade

```bash
curl -LO https://github.com/Lightbridge-KS/FleetManager/releases/download/v1.3.0/fleet-agent_1.3.0_amd64.deb
sudo dpkg -i fleet-agent_1.3.0_amd64.deb
# postinst restarts the service automatically
# /etc/fleet-agent/agent.env is preserved (conffile)
```

### Agent: Uninstall

```bash
# Remove (keeps config files)
sudo dpkg -r fleet-agent

# Purge (removes everything including config and user)
sudo dpkg -P fleet-agent
```

### CLI: Install on Admin Workstation (macOS Apple Silicon)

```bash
# Download from GitHub Releases
curl -LO https://github.com/Lightbridge-KS/FleetManager/releases/download/v1.2.0/fleet-ctl-macos-arm64.tar.gz
tar -xzf fleet-ctl-macos-arm64.tar.gz
mv fleet-ctl ~/bin/       # or /usr/local/bin/

# First-time setup
fleet-ctl login --url https://console.example.com
# Prompts: Enter API Key: ********
# Output: Logged in to https://console.example.com successfully.
#         Config saved to /Users/you/.config/fleet-ctl/config.json
```

### CLI: Daily Usage

```bash
# See all agents
fleet-ctl agents list

# Check one agent
fleet-ctl agents status agent-radiology-01

# Restart an app on an agent
fleet-ctl apps restart agent-radiology-01 radiology-ai

# Push config
fleet-ctl apps config agent-radiology-01 dicom-gateway \
  --set LogLevel=Debug --set MaxRetries=5

# Trigger update
fleet-ctl apps update agent-radiology-01 radiology-ai \
  --version 2.0.0 --artifact https://artifacts.example.com/v2.tar.gz

# View audit log
fleet-ctl audit --last 10 --agent agent-radiology-01

# Machine-readable output for scripting
fleet-ctl agents list --json | jq '.[] | select(.isOnline == true) | .agentId'
```

---

## 10. Acceptance Criteria

When implementation is complete, the following must work:

### Agent (.deb)
1. Running `./packaging/build-deb.sh 1.0.0` from repo root produces `./dist/fleet-agent_1.0.0_amd64.deb`
2. `dpkg-deb --info ./dist/fleet-agent_1.0.0_amd64.deb` shows correct metadata
3. `dpkg-deb --contents ./dist/fleet-agent_1.0.0_amd64.deb` shows files at correct paths
4. After install on a clean Ubuntu 22.04/24.04: `systemctl status fleet-agent` shows active
5. After upgrade with a new `.deb`: service restarts, `/etc/fleet-agent/agent.env` is preserved
6. After `dpkg -P fleet-agent`: service stopped, user removed, all files cleaned up

### CLI
7. `dotnet run --project src/Fleet.Cli -- --help` shows the full command tree
8. `fleet-ctl login --url http://localhost:5000` creates `~/.config/fleet-ctl/config.json`
9. `fleet-ctl agents list` returns a formatted table when the cloud console is running
10. `fleet-ctl agents list --json` returns valid JSON
11. `fleet-ctl apps restart agent-radiology-01 radiology-ai` sends the command and prints confirmation
12. `fleet-ctl audit --last 5` shows recent audit entries
13. Running any command without prior `fleet-ctl login` prints a clear error: `Not configured. Run 'fleet-ctl login' first.`

### CI/CD
14. Pushing a `v1.0.0` tag triggers the GitHub Actions workflow which runs test → build-deb + build-cli → release
15. The GitHub Release page shows: `.deb` file + three CLI tarballs (linux-x64, macos-x64, macos-arm64)
16. `scripts/install-from-github.sh` downloads and installs the latest agent release

---

## 11. Out of Scope (Future Work)

These items are explicitly NOT part of this spec:

- Private APT repository setup (Aptly, S3, Cloudsmith)
- ARM64 agent `.deb` build
- GPG signing of the `.deb` package
- Windows CLI build (`win-x64`)
- `fleet-ctl` as a `dotnet tool` (global tool install via NuGet)
- Homebrew formula for macOS CLI install
- Authentication implementation on Cloud Console API (JWT / API key middleware)
- Interactive TUI dashboard mode (e.g., `fleet-ctl dashboard` with Spectre.Console)
- Automated rollback on failed health check
- Cloud Console–initiated updates (the SignalR `UpdateApp` path)
- Integration tests that install the `.deb` in a VM/container
