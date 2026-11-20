#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ $# -lt 1 ]]; then
  echo "Usage: bash training/run-serious.sh /path/to/imitation/run [--minutes 90] [other PPO options]" >&2
  exit 1
fi
warm_start="$1"
shift
exec bash "$repo_root/training/run-sample.sh" ppo --warm-start "$warm_start" \
  --minutes 90 --updates 100000 --train playing --warmup-updates 24 \
  --decisions 32768 --bidding-decisions 8192 --pool 128 --controlled-percent 20 \
  --eval-seeds 500 --eval-every 10 --start-index 10000000 \
  --keep-checkpoints 4 --keep-rollouts 2 "$@"
