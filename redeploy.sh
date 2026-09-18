#!/usr/bin/env bash
# Redeploy hrdr as a systemd --user service on pi501.
#
# Run on pi501 from anywhere:
#   ~/Projects/hrdr/redeploy.sh
#
# Optional environment:
#   HRDR_ROOT   default ~/Projects/hrdr
set -euo pipefail

HRDR_ROOT="${HRDR_ROOT:-$HOME/Projects/hrdr}"
SRC_ROOT="${HRDR_ROOT}/src"
PROJECT="${SRC_ROOT}/src/Hrdr/Hrdr.csproj"
PUBLISH_DIR="${HRDR_ROOT}/publish"
ENV_SRC="${SRC_ROOT}/deploy/hrdr.env"
ENV_DST="${HRDR_ROOT}/hrdr.env"
UNIT_SRC="${SRC_ROOT}/deploy/hrdr.service"
UNIT_DST="${HOME}/.config/systemd/user/hrdr.service"
SERVICE="hrdr.service"
URL="${HRDR_URL:-http://127.0.0.1:5080}"

log() { printf '\n==> %s\n' "$*"; }
die() { printf 'error: %s\n' "$*" >&2; exit 1; }

dotnet_bin() {
  if command -v dotnet >/dev/null 2>&1; then
    command -v dotnet
    return
  fi
  if [[ -x "${HOME}/.dotnet/dotnet" ]]; then
    printf '%s\n' "${HOME}/.dotnet/dotnet"
    return
  fi
  die "dotnet SDK not found. Install .NET or add ~/.dotnet to PATH."
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
  cat <<EOF
Redeploy hrdr on this host as a systemd user service.

Usage:
  $(basename "$0")

Steps:
  1. Stop ${SERVICE}
  2. Publish Release build to ${PUBLISH_DIR}
  3. Refresh systemd unit (and create hrdr.env if missing)
  4. Restart ${SERVICE}
  5. Health-check ${URL}
EOF
  exit 0
fi

[[ -f "${PROJECT}" ]] || die "Project not found: ${PROJECT}"
[[ -f "${UNIT_SRC}" ]] || die "Service unit not found: ${UNIT_SRC}"

DOTNET="$(dotnet_bin)"
export PATH="$(dirname "${DOTNET}"):${PATH}"
export DOTNET_ROOT="${DOTNET_ROOT:-$(dirname "${DOTNET}")}"

log "Stopping ${SERVICE} (if running)"
systemctl --user stop "${SERVICE}" 2>/dev/null || true

log "Publishing Release build to ${PUBLISH_DIR}"
mkdir -p "${PUBLISH_DIR}" "${HRDR_ROOT}/data" "${HOME}/.config/systemd/user"
"${DOTNET}" publish "${PROJECT}" -c Release -o "${PUBLISH_DIR}"
[[ -f "${PUBLISH_DIR}/Hrdr.dll" ]] || die "Publish did not produce Hrdr.dll"

if [[ ! -f "${ENV_DST}" ]]; then
  [[ -f "${ENV_SRC}" ]] || die "Env template not found: ${ENV_SRC}"
  log "Creating ${ENV_DST} from template"
  cp "${ENV_SRC}" "${ENV_DST}"
else
  log "Keeping existing ${ENV_DST}"
fi

log "Installing systemd user unit"
cp "${UNIT_SRC}" "${UNIT_DST}"
systemctl --user daemon-reload
systemctl --user enable "${SERVICE}"

log "Starting ${SERVICE}"
systemctl --user restart "${SERVICE}"

log "Waiting for ${URL}"
ok=0
for _ in $(seq 1 30); do
  if curl -fsS --max-time 2 "${URL}/" >/dev/null 2>&1 \
    || curl -fsS --max-time 2 "${URL}/mcp" >/dev/null 2>&1; then
    ok=1
    break
  fi
  # App may return non-2xx on /; treat any TCP response as up.
  if curl -sS --max-time 2 -o /dev/null -w '%{http_code}' "${URL}/" | grep -qE '^[1-5][0-9]{2}$'; then
    ok=1
    break
  fi
  sleep 1
done

systemctl --user --no-pager --full status "${SERVICE}" || true

if [[ "${ok}" -eq 1 ]]; then
  printf '\nRedeployed. App: %s  MCP: %s/mcp\n' "${URL}" "${URL}"
else
  printf '\nService restarted but health check did not succeed yet.\n' >&2
  printf 'Check: systemctl --user status %s\n' "${SERVICE}" >&2
  printf 'Logs:  journalctl --user -u %s -n 50 --no-pager\n' "${SERVICE}" >&2
  printf '       tail -n 50 %s/hrdr.log\n' "${HRDR_ROOT}" >&2
  exit 1
fi
