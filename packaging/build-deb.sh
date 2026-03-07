#!/bin/bash
# build-deb.sh — Build a .deb package for the Fleet Agent
# Run from the FleetManager repo root:
#   ./packaging/build-deb.sh <version>
#   ./packaging/build-deb.sh 1.2.0
# Output: ./dist/fleet-agent_<version>_amd64.deb

set -euo pipefail

# ── Parse and validate version argument ──

VERSION="${1:-}"
ARCH="amd64"
PKG_NAME="fleet-agent"

if [[ -z "$VERSION" ]]; then
    echo "Usage: $0 <version>"
    echo "Example: $0 1.2.0"
    exit 1
fi

if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "Error: Version must match semver pattern N.N.N (e.g., 1.2.0)"
    echo "Got: $VERSION"
    exit 1
fi

STAGING_DIR="./tmp-deb/${PKG_NAME}_${VERSION}_${ARCH}"
PUBLISH_DIR="./tmp-publish"
OUTPUT_DIR="./dist"

echo "Building ${PKG_NAME} v${VERSION} (${ARCH})..."

# ── 1. Publish the .NET app ──

echo ">> Publishing Agent.Worker..."
dotnet publish src/Agent.Worker -c Release \
    --self-contained true \
    -r linux-x64 \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "$PUBLISH_DIR"

# ── 2. Create staging directory mirroring target filesystem ──

rm -rf "$STAGING_DIR"
mkdir -p "$STAGING_DIR"/{DEBIAN,opt/fleet-agent,etc/fleet-agent,etc/systemd/system,var/lib/fleet-agent}

# ── 3. Copy files into staging ──

# Application binary + config
cp "$PUBLISH_DIR/Agent.Worker"      "$STAGING_DIR/opt/fleet-agent/"
cp "$PUBLISH_DIR/appsettings.json"  "$STAGING_DIR/opt/fleet-agent/"

# systemd unit file
cp ./docs/fleet-agent.service       "$STAGING_DIR/etc/systemd/system/"

# Default environment config
cp ./packaging/etc/fleet-agent/agent.env.default "$STAGING_DIR/etc/fleet-agent/agent.env"

# DEBIAN packaging files
cp ./packaging/DEBIAN/conffiles     "$STAGING_DIR/DEBIAN/"
cp ./packaging/DEBIAN/postinst      "$STAGING_DIR/DEBIAN/"
cp ./packaging/DEBIAN/prerm         "$STAGING_DIR/DEBIAN/"
cp ./packaging/DEBIAN/postrm        "$STAGING_DIR/DEBIAN/"

# ── 4. Substitute version in control file ──

sed "s/{{VERSION}}/$VERSION/g" ./packaging/DEBIAN/control.template > "$STAGING_DIR/DEBIAN/control"

# ── 5. Set permissions ──

chmod 755 "$STAGING_DIR/DEBIAN/postinst"
chmod 755 "$STAGING_DIR/DEBIAN/prerm"
chmod 755 "$STAGING_DIR/DEBIAN/postrm"
chmod 644 "$STAGING_DIR/DEBIAN/control"
chmod 644 "$STAGING_DIR/DEBIAN/conffiles"

# ── 6. Build the .deb ──

mkdir -p "$OUTPUT_DIR"
dpkg-deb --build --root-owner-group "$STAGING_DIR" "$OUTPUT_DIR/"

echo ""
echo "Built: ${OUTPUT_DIR}/${PKG_NAME}_${VERSION}_${ARCH}.deb"
dpkg-deb --info "${OUTPUT_DIR}/${PKG_NAME}_${VERSION}_${ARCH}.deb"

# ── 7. Clean up ──

rm -rf "$PUBLISH_DIR" "./tmp-deb"
echo "Cleaned up temporary directories."
