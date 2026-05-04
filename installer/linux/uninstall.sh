#!/usr/bin/env bash
# ============================================================
# LambdaSQL — Linux uninstaller
# Stops and removes services, binaries, symlinks, user.
# Optionally removes data and logs.
#
# Usage:
#   sudo bash uninstall.sh [--keep-data]
# ============================================================

set -euo pipefail

INSTALL_DIR="/opt/lambdasql"
DATA_DIR="/var/lambdasql/data"
LOG_DIR="/var/log/lambdasql"
SERVICE_USER="lambdasql"
KEEP_DATA=false

while [[ $# -gt 0 ]]; do
  case $1 in
    --keep-data) KEEP_DATA=true; shift ;;
    *) echo "Unknown option: $1"; exit 1 ;;
  esac
done

if [[ $EUID -ne 0 ]]; then
  echo "Error: run as root (sudo bash uninstall.sh)" >&2
  exit 1
fi

echo ""
echo "╔══════════════════════════════════════╗"
echo "║   LambdaSQL Uninstaller              ║"
echo "╚══════════════════════════════════════╝"
echo ""

# ── Ask about data if not passed as flag ─────────────────────
if [[ "$KEEP_DATA" == false ]]; then
  read -r -p "Delete all data and logs in $DATA_DIR and $LOG_DIR? [y/N] " answer
  if [[ "$answer" =~ ^[Yy]$ ]]; then
    KEEP_DATA=false
  else
    KEEP_DATA=true
    echo "  Data will be kept."
  fi
fi

# ── Stop services ─────────────────────────────────────────────
echo "[1/5] Stopping services..."
for svc in lambdasql-server lambdasql-web; do
  if systemctl is-active --quiet "$svc" 2>/dev/null; then
    systemctl stop "$svc"
    echo "  Stopped $svc"
  fi
  if systemctl is-enabled --quiet "$svc" 2>/dev/null; then
    systemctl disable "$svc"
    echo "  Disabled $svc"
  fi
done

# ── Remove service files ──────────────────────────────────────
echo "[2/5] Removing systemd unit files..."
rm -f /etc/systemd/system/lambdasql-server.service
rm -f /etc/systemd/system/lambdasql-web.service
systemctl daemon-reload
systemctl reset-failed 2>/dev/null || true

# ── Remove symlink ────────────────────────────────────────────
echo "[3/5] Removing CLI symlink..."
rm -f /usr/local/bin/lambdasql

# ── Remove binaries ───────────────────────────────────────────
echo "[4/5] Removing install directory..."
if [[ -d "$INSTALL_DIR" ]]; then
  rm -rf "$INSTALL_DIR"
  echo "  Removed $INSTALL_DIR"
fi

# ── Remove data / logs ────────────────────────────────────────
echo "[5/5] Cleaning up data..."
if [[ "$KEEP_DATA" == false ]]; then
  rm -rf "$DATA_DIR"
  rm -rf "$LOG_DIR"
  # Remove parent dir if empty
  rmdir /var/lambdasql 2>/dev/null || true
  echo "  Removed $DATA_DIR and $LOG_DIR"
else
  echo "  Kept $DATA_DIR and $LOG_DIR"
fi

# ── Remove system user ────────────────────────────────────────
if id "$SERVICE_USER" &>/dev/null; then
  userdel "$SERVICE_USER" 2>/dev/null || true
  echo "  Removed user '$SERVICE_USER'"
fi

echo ""
echo "✓ LambdaSQL has been uninstalled."
if [[ "$KEEP_DATA" == true ]]; then
  echo "  Your data is still at: $DATA_DIR"
fi
echo ""
