#!/usr/bin/env bash
set -euo pipefail
if [[ $# -ne 1 || ! -f "$1/progress.log" ]]; then
  echo "Usage: bash training/watch-training.sh /path/to/run (must contain progress.log)" >&2
  exit 1
fi
echo "Watching $1/progress.log. Ctrl+C stops this viewer, not training."
exec tail -n 40 -F -- "$1/progress.log"
