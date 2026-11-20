#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
training_python="${SCHAFKOPF_PYTHON:-$HOME/.venvs/pax-schafkopf/bin/python}"
if [[ ! -x "$training_python" ]]; then
  echo "Missing training Python: $training_python. See training/README.md for WSL setup." >&2
  exit 1
fi
cd -- "$repo_root"
exec "$training_python" -u "$repo_root/training/train_sample.py" "$@"
