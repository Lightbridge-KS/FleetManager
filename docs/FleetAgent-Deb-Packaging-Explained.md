# Fleet Agent: `.deb` Packaging Explained

This document explains how the Fleet Agent is packaged as a `.deb` file and deployed via GitHub Actions. It walks through every file involved, what happens at build time on the CI Linux VM, and what happens at install time on the target Ubuntu server.

---

## The Big Picture

```
Developer's Machine              GitHub Actions (Ubuntu VM)           Target Ubuntu Server
──────────────────               ─────────────────────────           ────────────────────
git tag v1.2.0                   1. Test: build + test solution
git push --tags          ───►    2. Build: dotnet publish            curl -LO ...deb
                                    + assemble .deb package   ───►   sudo dpkg -i fleet-agent_1.2.0_amd64.deb
                                 3. Release: upload .deb to               │
                                    GitHub Releases (draft)               ▼
                                                                     Agent running as
                                                                     systemd service
```

The key insight: **we cannot build `.deb` packages on macOS** because `dpkg-deb` (the Debian packaging tool) is a Linux-only utility. That's why the build happens on a GitHub Actions `ubuntu-latest` runner, which has `dpkg-deb` pre-installed.

---

## What is a `.deb` Package?

A `.deb` file is the standard package format for Debian-based Linux distributions (Ubuntu, Debian, etc.). It's essentially an archive containing:

1. **Application files** — placed at their final filesystem locations
2. **Metadata** — package name, version, dependencies
3. **Lifecycle scripts** — run automatically during install, upgrade, and removal

When an admin runs `sudo dpkg -i fleet-agent_1.2.0_amd64.deb`, the system:
1. Extracts files to the filesystem
2. Runs the `postinst` script
3. The agent is now installed, enabled, and running

---

## Files in `packaging/` — Purpose of Each

### Directory Structure

```
packaging/
├── build-deb.sh                     # Build script (orchestrates everything)
├── DEBIAN/                          # Debian packaging metadata & scripts
│   ├── control.template             #   Package metadata (name, version, deps)
│   ├── conffiles                    #   List of config files to preserve on upgrade
│   ├── postinst                     #   Runs AFTER package files are placed on disk
│   ├── prerm                        #   Runs BEFORE package files are removed
│   └── postrm                       #   Runs AFTER package files are removed
└── etc/
    └── fleet-agent/
        └── agent.env.default        # Default environment config template
```

---

### `DEBIAN/control.template`

**Purpose**: Package metadata — tells `dpkg` what this package is.

```
Package: fleet-agent
Version: {{VERSION}}          ◄── Replaced by build-deb.sh at build time
Section: admin
Priority: optional
Architecture: amd64
Depends: libicu74 | libicu72 | libicu70, libssl3 | libssl1.1
Maintainer: FleetManager Team <infra@example.com>
Homepage: https://github.com/Lightbridge-KS/FleetManager
Description: Fleet Manager Agent
 Lightweight agent daemon that connects to the Fleet Manager
 cloud console for remote monitoring, configuration, and
 application lifecycle management.
```

Key fields:
- **Package**: The name used by `dpkg -i`, `dpkg -r`, `apt list`
- **Version**: `{{VERSION}}` is a placeholder — `build-deb.sh` substitutes it with the actual version (e.g., `1.2.0`) using `sed` at build time
- **Architecture**: `amd64` — this is an x86_64 binary
- **Depends**: Runtime libraries the .NET app needs. The `|` means "any one of these" — covers Ubuntu 20.04 through 24.04

Why is it a `.template` and not just `control`? Because the version changes with each release. The template lives in source control with `{{VERSION}}`; the real `control` file is generated per build.

---

### `DEBIAN/conffiles`

**Purpose**: Tells `dpkg` which files are **admin-editable config** that should be preserved during upgrades.

```
/etc/fleet-agent/agent.env
```

Without this file, `dpkg -i` would overwrite the admin's config every time they upgrade. With it, dpkg detects if the admin has modified `agent.env` and either keeps their version or prompts them to choose.

This is critical because `agent.env` contains deployment-specific values like the Hub URL and Agent ID.

---

### `DEBIAN/postinst`

**Purpose**: Runs **after** the package files are placed on disk. Handles first-time setup and upgrades.

```
What it does (in order):
1. Create 'fleet-agent' system user        ◄── Only on first install
2. Create /var/lib/fleet-agent directory   ◄── Agent's writable data dir
3. Set file ownership to fleet-agent user  ◄── Security: agent owns its files
4. Set agent.env to mode 600              ◄── Only the agent can read config
5. Make the binary executable              ◄── chmod +x
6. systemctl daemon-reload                 ◄── Tell systemd about the service
7. systemctl enable fleet-agent            ◄── Start on boot
8. Start or restart the service            ◄── Immediate activation
```

The script is idempotent — running it twice (upgrade scenario) won't create duplicate users or break anything.

---

### `DEBIAN/prerm`

**Purpose**: Runs **before** package files are removed. Gracefully stops the service.

```
What it does:
1. Stop the service (if running)
2. Disable the service (ignore errors if already disabled)
```

This ensures the binary isn't in use when dpkg tries to delete it.

---

### `DEBIAN/postrm`

**Purpose**: Runs **after** package files are removed. Handles cleanup.

```
What it does:
1. On 'purge' only:  remove fleet-agent user, /var/lib/fleet-agent, /etc/fleet-agent
2. Always:           systemctl daemon-reload
```

Debian has two levels of removal:
- `dpkg -r fleet-agent` — **remove**: deletes binaries but keeps config files
- `dpkg -P fleet-agent` — **purge**: deletes everything including config and user

The `$1` argument tells the script which operation is happening.

---

### `etc/fleet-agent/agent.env.default`

**Purpose**: Default environment config shipped in the package. At build time, this gets copied to `agent.env` inside the `.deb`.

```bash
DOTNET_ENVIRONMENT=Production
CloudConsole__HubUrl=https://console.example.com/hub/fleet
Agent__Id=agent-CHANGEME
Agent__HeartbeatIntervalSec=30
```

After install, the admin edits `/etc/fleet-agent/agent.env` with real values. The `__` (double underscore) is .NET's convention for nested config — `CloudConsole__HubUrl` maps to `CloudConsole:HubUrl` in `appsettings.json`.

---

### `build-deb.sh`

**Purpose**: The orchestrator — assembles the `.deb` from published .NET output and packaging templates.

```
Input:   version argument (e.g., "1.2.0")
Output:  ./dist/fleet-agent_1.2.0_amd64.deb

Steps:
┌─────────────────────────────────────────────────────────────────┐
│ 1. Validate version (must be N.N.N)                             │
│                                                                 │
│ 2. dotnet publish ──► ./tmp-publish/Agent.Worker (single file)  │
│                                                                 │
│ 3. Create staging dir mirroring the target filesystem:          │
│    ./tmp-deb/fleet-agent_1.2.0_amd64/                           │
│    ├── DEBIAN/              ◄── metadata + scripts              │
│    ├── opt/fleet-agent/     ◄── binary + appsettings.json       │
│    ├── etc/fleet-agent/     ◄── agent.env                       │
│    ├── etc/systemd/system/  ◄── fleet-agent.service             │
│    └── var/lib/fleet-agent/ ◄── empty data directory            │
│                                                                 │
│ 4. Copy all files into staging                                  │
│ 5. sed: replace {{VERSION}} in control file                     │
│ 6. Set permissions (755 for scripts, 644 for metadata)          │
│                                                                 │
│ 7. dpkg-deb --build --root-owner-group ──► .deb file            │
│                                                                 │
│ 8. Print package info                                           │
│ 9. Clean up tmp dirs                                            │
└─────────────────────────────────────────────────────────────────┘
```

Key flags on `dotnet publish`:
- `--self-contained true` — bundles the .NET runtime (no runtime needed on server)
- `-r linux-x64` — cross-compile for Linux even if building on another OS
- `-p:PublishSingleFile=true` — one executable file instead of hundreds of DLLs
- `-p:IncludeNativeLibrariesForSelfExtract=true` — native libs bundled inside

Key flag on `dpkg-deb`:
- `--root-owner-group` — all files in the `.deb` are owned by `root:root` regardless of who built it. The `postinst` script then sets correct ownership (`fleet-agent:fleet-agent`) at install time.

---

## Other Files Used by the Build

### `docs/fleet-agent.service`

The systemd unit file — defines how Linux manages the agent as a service.

```ini
[Service]
Type=notify                              # Agent signals readiness (AddSystemd())
ExecStart=/opt/fleet-agent/Agent.Worker  # The binary to run
EnvironmentFile=/etc/fleet-agent/agent.env  # Config via env vars
User=fleet-agent                         # Run as dedicated user (not root)

# Security: restrict what the process can do
ProtectSystem=strict       # Filesystem is read-only except explicit paths
ProtectHome=true           # Cannot access /home
PrivateTmp=true            # Gets its own /tmp
NoNewPrivileges=true       # Cannot escalate privileges
ReadWritePaths=/var/lib/fleet-agent  # Only this dir is writable
ReadOnlyPaths=/opt/fleet-agent       # Binary dir is read-only
```

`Type=notify` works with `AddSystemd()` in the .NET code — the agent tells systemd "I'm ready" after initialization rather than systemd guessing based on process start.

---

## The GitHub Actions Workflow

### Pipeline

```
push tag v1.2.0
    │
    ▼
┌──────────┐     ┌─────────────┐     ┌──────────────┐
│   test   │────►│  build-deb  │────►│   release    │
│          │     │             │     │              │
│ restore  │     │ dotnet      │     │ download     │
│ build    │     │   publish   │     │   artifact   │
│ test     │     │ build-deb.sh│     │ create draft │
│          │     │ upload      │     │   GH release │
│          │     │   artifact  │     │ attach .deb  │
└──────────┘     └─────────────┘     └──────────────┘
     VM 1              VM 2                VM 3
```

Each job runs on a **separate** Ubuntu VM. They share files via GitHub's artifact storage.

### Job 1: `test`

Standard .NET CI: restore → build → test. If tests fail, the pipeline stops here.

### Job 2: `build-deb`

1. **Extract version from tag**: `v1.2.0` → `1.2.0` (strips the `v` prefix)
2. **Run `build-deb.sh`**: publishes the .NET app and assembles the `.deb`
3. **Upload artifact**: saves the `.deb` file to GitHub's temporary artifact storage so the next job can access it

The version extraction uses environment variables (`env: REF_NAME`) instead of inline `${{ }}` in shell commands — this is a security best practice to prevent shell injection.

### Job 3: `release`

1. **Download artifact**: retrieves the `.deb` from artifact storage
2. **Create GitHub Release**: uses `softprops/action-gh-release` to create a **draft** release with auto-generated release notes and the `.deb` attached

The release is a draft so the maintainer can review before publishing.

---

## What Happens on the Target Ubuntu Server

### Install

```bash
sudo dpkg -i fleet-agent_1.2.0_amd64.deb
```

```
dpkg extracts files:
  /opt/fleet-agent/Agent.Worker          # binary
  /opt/fleet-agent/appsettings.json      # app config
  /etc/fleet-agent/agent.env             # env config
  /etc/systemd/system/fleet-agent.service # systemd unit
  /var/lib/fleet-agent/                  # data dir (empty)
         │
         ▼
dpkg runs postinst:
  create fleet-agent user
  chown files → fleet-agent:fleet-agent
  chmod 600 agent.env
  systemctl daemon-reload
  systemctl enable fleet-agent
  systemctl start fleet-agent
         │
         ▼
Agent is running! ✓
```

### Upgrade

```bash
sudo dpkg -i fleet-agent_1.3.0_amd64.deb
```

```
dpkg sees fleet-agent is already installed
  → checks conffiles: agent.env was modified by admin? → KEEP admin's version
  → replaces binary and service file
  → runs postinst → restarts the service
```

### Remove vs Purge

```
dpkg -r fleet-agent              dpkg -P fleet-agent
───────────────────              ───────────────────
prerm: stop + disable            prerm: stop + disable
remove: /opt/fleet-agent/        remove: /opt/fleet-agent/
        /etc/systemd/system/             /etc/systemd/system/
keep:   /etc/fleet-agent/        postrm (purge):
postrm: daemon-reload              delete /etc/fleet-agent/
                                   delete /var/lib/fleet-agent/
                                   delete fleet-agent user
                                   daemon-reload
```
