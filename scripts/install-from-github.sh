#!/bin/bash
# install-from-github.sh — Download and install the Fleet Agent .deb from GitHub Releases
#
# Usage:
#   curl -fsSL https://raw.githubusercontent.com/Lightbridge-KS/FleetManager/main/scripts/install-from-github.sh | sudo bash
#   curl -fsSL https://raw.githubusercontent.com/Lightbridge-KS/FleetManager/main/scripts/install-from-github.sh | sudo bash -s -- 1.2.0

set -euo pipefail

REPO="Lightbridge-KS/FleetManager"
VERSION="${1:-}"

# ── 1. Determine version ──

if [[ -z "$VERSION" ]]; then
    echo "Querying GitHub for latest release..."
    VERSION=$(curl -s "https://api.github.com/repos/${REPO}/releases/latest" \
        | grep '"tag_name"' \
        | sed -E 's/.*"v([^"]+)".*/\1/')

    if [[ -z "$VERSION" ]]; then
        echo "Error: Could not determine latest version from GitHub."
        exit 1
    fi
fi

echo "Installing fleet-agent v${VERSION}..."

# ── 2. Download .deb ──

DEB_URL="https://github.com/${REPO}/releases/download/v${VERSION}/fleet-agent_${VERSION}_amd64.deb"
DEB_PATH="/tmp/fleet-agent.deb"

echo "Downloading: $DEB_URL"
curl -fSL -o "$DEB_PATH" "$DEB_URL"

if [[ ! -s "$DEB_PATH" ]]; then
    echo "Error: Downloaded file is empty or missing."
    exit 1
fi

# ── 3. Install ──

echo "Installing..."
dpkg -i "$DEB_PATH"

# ── 4. Clean up ──

rm -f "$DEB_PATH"

# ── 5. Show status ──

echo ""
echo "========================================="
systemctl status fleet-agent --no-pager || true
echo "========================================="
echo ""
echo "IMPORTANT: Edit /etc/fleet-agent/agent.env with your actual values:"
echo "  sudo nano /etc/fleet-agent/agent.env"
echo ""
echo "Then restart the service:"
echo "  sudo systemctl restart fleet-agent"
