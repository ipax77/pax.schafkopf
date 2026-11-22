#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
training_python="${SCHAFKOPF_PYTHON:-$HOME/.venvs/pax-schafkopf/bin/python}"
if [[ $# -ne 1 ]]; then
  echo "Usage: bash training/play-trained.sh /path/to/PPO/run-or-exported-pair-directory" >&2
  exit 1
fi
cd -- "$repo_root"
# Do not replace another server or interrupt an existing table.
"$training_python" - <<'PY'
import socket
for port in (5283, 5027):
    with socket.socket() as probe:
        try:
            probe.bind(('127.0.0.1', port))
        except OSError:
            raise SystemExit(f'Port {port} is in use. Stop the existing local API/web app before starting trained play.')
PY
mkdir -p "$HOME/.local/share/pax-schafkopf"
play_dir="$(mktemp -d "$HOME/.local/share/pax-schafkopf/play-XXXXXXXX")"
if [[ -f "$1/pair.json" ]] && "$training_python" -c 'import json,sys; sys.exit(json.load(open(sys.argv[1])).get("native_format") != "dense-f32-v1")' "$1/pair.json"; then
  manifest="$(realpath -- "$1/pair.json")"
else
  "$training_python" training/export_policy.py "$1" --output "$play_dir/models"
  manifest="$play_dir/models/pair.json"
fi
bash training/dotnet.sh build src/sk.api/sk.api.csproj -c Release --artifacts-path "$play_dir/build" -v quiet
bash training/dotnet.sh build src/sk.pwa/sk.pwa.csproj -c Release --artifacts-path "$play_dir/build" -v quiet
api_pid=""
web_pid=""
cleanup() {
  trap - EXIT INT TERM
  [[ -z "$api_pid" ]] || kill -- "-$api_pid" 2>/dev/null || true
  [[ -z "$web_pid" ]] || kill -- "-$web_pid" 2>/dev/null || true
  wait 2>/dev/null || true
}
trap cleanup EXIT INT TERM
ASPNETCORE_ENVIRONMENT=Development Bots__ModelManifest="$manifest" \
  setsid bash training/dotnet.sh run --project src/sk.api -c Release --no-build \
    --artifacts-path "$play_dir/build" --no-launch-profile --urls http://localhost:5283 > "$play_dir/api.log" 2>&1 &
api_pid=$!
ASPNETCORE_ENVIRONMENT=Development \
  setsid bash training/dotnet.sh run --project src/sk.pwa -c Release --no-build \
    --artifacts-path "$play_dir/build" --no-launch-profile --urls http://localhost:5027 > "$play_dir/web.log" 2>&1 &
web_pid=$!
echo "Open http://localhost:5027, create a table, then select 'Freie Plätze mit Bots füllen'."
echo "Frozen model: $manifest"
echo "Policy status: http://localhost:5283/bots/policy"
echo "Logs: $play_dir/api.log and $play_dir/web.log"
echo "Keep this terminal open. Ctrl+C stops these two servers; training is separate."
wait -n "$api_pid" "$web_pid"
