#!/usr/bin/env bash
# ============================================================
# LambdaSQL — Linux installer
# Installs: Server, Web UI, CLI
# Registers systemd services for Server and Web UI
# Adds CLI to /usr/local/bin
#
# Usage:
#   sudo bash install.sh [--data /var/lambdasql/data] [--port 5464] [--web-port 5000]
# ============================================================

set -euo pipefail

# ── Defaults ─────────────────────────────────────────────────
INSTALL_DIR="/opt/lambdasql"
DATA_DIR="/var/lambdasql/data"
LOG_DIR="/var/log/lambdasql"
SERVER_PORT=5464
WEB_PORT=5000
SERVICE_USER="lambdasql"
VERSION="1.0.0"

# ── Parse args ───────────────────────────────────────────────
while [[ $# -gt 0 ]]; do
  case $1 in
    --data)      DATA_DIR="$2";    shift 2 ;;
    --port)      SERVER_PORT="$2"; shift 2 ;;
    --web-port)  WEB_PORT="$2";    shift 2 ;;
    --dir)       INSTALL_DIR="$2"; shift 2 ;;
    *) echo "Unknown option: $1"; exit 1 ;;
  esac
done

# ── Must run as root ─────────────────────────────────────────
if [[ $EUID -ne 0 ]]; then
  echo "Error: run as root (sudo bash install.sh)" >&2
  exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo ""
echo "╔══════════════════════════════════════╗"
echo "║   LambdaSQL Installer v${VERSION}       ║"
echo "╚══════════════════════════════════════╝"
echo ""
echo "  Install dir : $INSTALL_DIR"
echo "  Data dir    : $DATA_DIR"
echo "  Server port : $SERVER_PORT"
echo "  Web port    : $WEB_PORT"
echo ""

# ── Create system user ────────────────────────────────────────
if ! id "$SERVICE_USER" &>/dev/null; then
  echo "[1/7] Creating system user '$SERVICE_USER'..."
  useradd --system --no-create-home --shell /usr/sbin/nologin "$SERVICE_USER"
else
  echo "[1/7] User '$SERVICE_USER' already exists."
fi

# ── Create directories ────────────────────────────────────────
echo "[2/7] Creating directories..."
mkdir -p "$INSTALL_DIR/server"
mkdir -p "$INSTALL_DIR/web"
mkdir -p "$INSTALL_DIR/cli"
mkdir -p "$DATA_DIR"
mkdir -p "$LOG_DIR"

# ── Copy binaries ─────────────────────────────────────────────
echo "[3/7] Copying binaries..."
cp -r "$SCRIPT_DIR/server/." "$INSTALL_DIR/server/"
cp -r "$SCRIPT_DIR/web/."    "$INSTALL_DIR/web/"
cp -r "$SCRIPT_DIR/cli/."    "$INSTALL_DIR/cli/"

chmod +x "$INSTALL_DIR/server/lambdasql-server"
chmod +x "$INSTALL_DIR/web/lambdasql-web"
chmod +x "$INSTALL_DIR/cli/lambdasql"

# ── Set ownership ─────────────────────────────────────────────
echo "[4/7] Setting permissions..."
chown -R "$SERVICE_USER:$SERVICE_USER" "$INSTALL_DIR"
chown -R "$SERVICE_USER:$SERVICE_USER" "$DATA_DIR"
chown -R "$SERVICE_USER:$SERVICE_USER" "$LOG_DIR"

# ── Symlink CLI ───────────────────────────────────────────────
echo "[5/7] Linking CLI to /usr/local/bin/lambdasql..."
ln -sf "$INSTALL_DIR/cli/lambdasql" /usr/local/bin/lambdasql

# ── systemd: Server ───────────────────────────────────────────
echo "[6/7] Installing systemd services..."

cat > /etc/systemd/system/lambdasql-server.service << EOF
[Unit]
Description=LambdaSQL Database Server
After=network.target
StartLimitIntervalSec=60
StartLimitBurst=3

[Service]
Type=simple
User=${SERVICE_USER}
Group=${SERVICE_USER}
WorkingDirectory=${INSTALL_DIR}/server
ExecStart=${INSTALL_DIR}/server/lambdasql-server --host 0.0.0.0 --port ${SERVER_PORT} --data ${DATA_DIR}
Restart=on-failure
RestartSec=5
StandardOutput=append:${LOG_DIR}/server.log
StandardError=append:${LOG_DIR}/server-error.log
SyslogIdentifier=lambdasql-server

# Hardening
NoNewPrivileges=true
ProtectSystem=strict
ReadWritePaths=${DATA_DIR} ${LOG_DIR}
PrivateTmp=true

[Install]
WantedBy=multi-user.target
EOF

# ── systemd: Web UI ───────────────────────────────────────────
cat > /etc/systemd/system/lambdasql-web.service << EOF
[Unit]
Description=LambdaSQL Web UI
After=network.target lambdasql-server.service
StartLimitIntervalSec=60
StartLimitBurst=3

[Service]
Type=simple
User=${SERVICE_USER}
Group=${SERVICE_USER}
WorkingDirectory=${INSTALL_DIR}/web
ExecStart=${INSTALL_DIR}/web/lambdasql-web --urls=http://0.0.0.0:${WEB_PORT} --data=${DATA_DIR}
Restart=on-failure
RestartSec=5
StandardOutput=append:${LOG_DIR}/web.log
StandardError=append:${LOG_DIR}/web-error.log
SyslogIdentifier=lambdasql-web

NoNewPrivileges=true
ProtectSystem=strict
ReadWritePaths=${DATA_DIR} ${LOG_DIR}
PrivateTmp=true

[Install]
WantedBy=multi-user.target
EOF

# ── Enable and start ──────────────────────────────────────────
echo "[7/7] Enabling and starting services..."
systemctl daemon-reload
systemctl enable lambdasql-server lambdasql-web
systemctl start  lambdasql-server lambdasql-web

# ── Done ──────────────────────────────────────────────────────
echo ""
echo "✓ LambdaSQL installed successfully!"
echo ""
echo "  Services:"
echo "    systemctl status lambdasql-server"
echo "    systemctl status lambdasql-web"
echo ""
echo "  Web UI:  http://localhost:${WEB_PORT}"
echo "  Server:  localhost:${SERVER_PORT}"
echo "  CLI:     lambdasql --host localhost --port ${SERVER_PORT}"
echo ""
echo "  Logs:    $LOG_DIR"
echo "  Data:    $DATA_DIR"
echo ""
echo "  To uninstall: sudo bash uninstall.sh"
echo ""
