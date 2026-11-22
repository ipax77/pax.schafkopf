#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ $# -lt 1 ]]; then
  echo "Usage: bash training/run-gameplay.sh /path/to/imitation/run-or-checkpoint [--minutes 90] [other PPO options]" >&2
  exit 1
fi
warm_start="$1"
shift
exec bash "$repo_root/training/run-sample.sh" ppo --warm-start "$warm_start" \
  --train playing --bidder conservative --opponents mixed --self-play-percent 25 \
  --controlled-percent 40 --decisions 32768 --pool 128 --playing-learning-rate 0.0001 \
  --eval-suite gameplay --eval-start-index 3000000 --eval-seeds 64 --eval-every 20 \
  --start-index 40000000 --keep-checkpoints 4 --keep-rollouts 2 \
  --updates 100000 --minutes 90 "$@"
