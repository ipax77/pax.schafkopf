# Initial baseline — 2026-11-20

These are development/validation runs, not trained-model promotion results. Reports record
the source commit plus a dirty-tree flag because implementation changes were still uncommitted.
Policy versions and full evaluation options are saved in each JSON file. No policy thresholds
were tuned after examining these results.

## Environment

[environment.json](environment.json) records the successful full preflight:

- WSL Ubuntu, selected SDK 10.0.404, .NET runtime 10.0.15.
- Isolated Python 3.12.3 at `~/.venvs/pax-schafkopf`, TensorFlow 2.21.0.
- RTX 3080 Ti, 12,288 MiB VRAM; TensorFlow executed a matrix multiplication on GPU.
- `pip check` passed. Resolved versions are in [the lock file](../requirements-wsl-py312.lock).

All **83 regression tests passed**: the original 67 plus 16 bot/evaluation tests. Existing
Microsoft.OpenApi/OpenTelemetry NuGet audit warnings remain; the new projects add no packages.

## Engine benchmark

[engine-sdk10.0.404.json](engine-sdk10.0.404.json) re-establishes the engine baseline with the
selected SDK and isolated WSL build outputs. Tests and other benchmarks were not run concurrently.
All six checksums match the original engine benchmark. Median full-game times were 11.530 µs
without per-decision observations and 20.292 µs with observations; these exclude bot decisions,
transport, and training. Both full-game checksums remain `49792B2969824678`.

## Heuristic comparisons

Each row uses 500 validation base seeds, all four dealers, and all four focal seats:
16,000 games per paired comparison, 48,000 in total. Intervals use 2,000 bootstrap resamples of
base-seed blocks. Units are raw engine tariff units per focal-seat game, including all-pass.

| Candidate | Reference and other three seats | Mean paired improvement | 95% interval | Report |
| --- | --- | ---: | --- | --- |
| Balanced | Conservative | -1.026 | [-1.730, -0.340] | [JSON](balanced-vs-conservative.json) |
| Conservative | Balanced | +1.470 | [0.820, 2.205] | [JSON](conservative-vs-balanced.json) |
| Conservative | Uniform legal random | +57.127 | [54.995, 59.226] | [JSON](conservative-vs-random.json) |

Conservative is the recommended initial style for live integration based on these limited
validation comparisons. Both heuristics use the same card-playing policy, so their difference
here comes from bidding. A positive result against a sibling heuristic is not evidence of
expert play or of compatibility with human partners.

Random bidding includes very poor Tout declarations. The large random-baseline margin must
not be used as a strength claim. Reference tables have four copies of the same policy, hence
zero average reference payout after focal-seat rotation; this is a useful symmetry check.

[controlled-ruf.json](controlled-ruf.json) is a separate CLI/trace check with identical forced
Ruf Schellen auctions, rotating declarers, and mixed opponents. Balanced and conservative have
identical card play, so their paired improvement and its interval must be exactly zero.
It completed 1,856 games from 58 eligible base seeds; 42 ineligible Ruf deals were counted and
excluded before either candidate/reference game ran.

The evaluator aborts on an illegal action or stalled episode. All recorded runs completed,
and their example action traces replayed with identical terminal payouts. This validates the
baseline infrastructure. Live seats and scheduling were subsequently implemented; neural serving
latency and longer human strength playtests remain pending.

## First neural imitation sample — 2026-11-20

The sample runner completed 2,000 natural conservative-teacher training games and 256 separate
validation games, producing 63,038 decisions across both splits. Both models trained for three
epochs on the RTX 3080 Ti with a 2 GiB TensorFlow memory cap, using the selected .NET SDK
10.0.404, Python 3.12.3, and TensorFlow 2.21.0. Total preflight/build/data/train/evaluation time
was 22.27 seconds. The run used about 503 MiB on the Linux filesystem.

| Held-out metric | Initial | Epoch 3 |
| --- | ---: | ---: |
| Bidding choice accuracy | 63.27% | 77.13% |
| Playing choice accuracy | 35.25% | 70.96% |
| Bidding masked cross-entropy | 0.9778 | 0.7490 |
| Playing masked cross-entropy | 1.2326 | 0.7862 |
| Bidding terminal-value MSE | 0.1321 | 0.1156 |
| Playing terminal-value MSE | 0.1563 | 0.0928 |

Accuracy excludes forced moves. These numbers measure imitation of the conservative teacher,
not optimal play. [Per-epoch metrics](sample-imitation-metrics.jsonl) retain exact values.

The [saved paired evaluation](sample-imitation-evaluation.json) used validation indices
1,000,000..1,000,031, disjoint from demonstration validation. Across all seats and dealers,
512 candidate games and 512 reference games completed with zero illegal actions. The trained
pair's raw payout and paired improvement were both **-2.265625 units/deal**, with 95%
base-seed bootstrap interval **[-3.4375, -1.015625]**. Candidate games included 258 normal Ruf,
12 Wenz, 10 Solo, and 232 all-pass. This small evaluation does not certify playing strength;
the model scored below its teacher and was not promoted to live play.

The resumable run, full environment manifest, data, and models are stored locally at:

```text
/home/pax77/.local/share/pax-schafkopf/runs/sample-20261120-202018-24475a
```

Source baseline: `42d1e44` plus the sample-training changes in the working tree. Master seed:
20261120. Rules: `schafkopf-long-v1`; encoder: `schafkopf-observation-v1`; teacher:
`heuristic-v1/conservative`. Both selected models came from epoch 3; inference used masked
argmax. Model/data files are intentionally kept out of Git. The companion 64-game smoke run
was stopped at a completed epoch and resumed in a new process, preserving Adam iteration
counts (bidding 1 → 2, playing 3 → 6). Checkpoint reload checks verify optimizer tensors and
model outputs after every saved epoch. Regression validation: 98 C# tests and 3 TensorFlow
objective tests passed.

## First warm-started PPO sample — 2026-11-20

The default playing PPO run completed ten updates from the imitation pair above, keeping
the bidder frozen. It collected **49,152 learner decisions from 4,040 complete training
games**, starting at training index 2,000,000; the next index is 2,004,040. The target was
4,096 decisions/update with a pool of 64 games; draining active games accounts for the extra
decisions. Tables alternated in balanced blocks between self-play and one learner against
three conservative heuristics. There were no historical opponents or controlled contracts.

The selected .NET SDK 10.0.404, Python 3.12.3, TensorFlow 2.21.0, and RTX 3080 Ti passed the
full preflight. TensorFlow used a 2 GiB allocator cap. The run recorded 51.66 seconds of
monotonic elapsed time, including preflight/build/evaluations, and about 117.5 MiB of files.
All ten updates completed four epochs. Final-update approximate KL was 0.00814, clip fraction
11.27%, entropy 0.881, value MSE 0.4955, and explained variance 0.6644. These training diagnostics
measure the sampled rollout, not strength. See [per-update metrics](sample-ppo-metrics.jsonl).

The initial, fifth-update, and final evaluations each used 32 validation seed blocks across
four seats and dealers (1,024 games each). The [final evaluation](sample-ppo-evaluation.json)
used masked argmax and had zero illegal actions:

| Comparison | Raw payout change per deal | 95% base-seed bootstrap interval |
| --- | ---: | --- |
| Final PPO pair vs conservative reference | -2.46094 | [-3.69189, -1.19092] |
| Final PPO pair vs its imitation warm start on the same panel | -0.19531 | [-0.52734, 0.11719] |

The sample remains below the teacher and shows **no clear improvement over its warm start**.
It is a correctness run, not a strength or promotion result. Live bots remain conservative
heuristics. Longer curricula and independent confirmation are needed before deployment.

All 4,040 saved game traces were replayed through the C# engine with identical payouts.
Every recorded return was checked against its own seat's terminal payout / 100, including
terminal placement, learner-only membership, and legal sampled actions. Checkpoint versions
matched each rollout; bidding weights and its zero Adam-step count stayed unchanged. CPU
re-evaluation of the old GPU policy statistics differed slightly: maximum absolute difference
0.001026 in log probability and 0.000815 in value. This is a numerical cross-check, not a
claim of bitwise CPU/GPU identity. Training retains the original collection statistics.

A separate small alternating run completed a playing update, exited, and resumed in a new
process for bidding. It restored the saved NumPy RNG and next deal index 2,000,023. Adam
iterations changed from bidding/play **0/4 to 4/4** while the playing weights stayed identical.
The checkpoint unit test also verified identical subsequent CPU updates after restoring
weights, Adam tensors, and RNG. Regression validation: **103 C# tests and 12 Python tests passed**.
The [verification record](sample-ppo-verification.json) retains counters and numerical checks.

The full resumable sample is on the Linux filesystem:

```text
/home/pax77/.local/share/pax-schafkopf/runs/ppo-20261120-205437-099b55
```

Continue it from the repository root in WSL, or watch its saved/current progress:

```bash
run="$HOME/.local/share/pax-schafkopf/runs/ppo-20261120-205437-099b55"
bash training/run-sample.sh ppo --resume "$run" --updates 20
# In another terminal, using the same run path:
bash training/watch-training.sh "$HOME/.local/share/pax-schafkopf/runs/ppo-20261120-205437-099b55"
```

Source baseline: `641da85` plus the PPO changes in the working tree. Master seed: 20261120;
rules: `schafkopf-long-v1`; encoder: `schafkopf-observation-v1`. Final playing optimizer steps:
120. Models and full rollouts are intentionally excluded from Git. See the
[launch/watch/resume guide](../README.md#ppo-from-imitation-checkpoints) for fresh runs and
bidding/alternating modes.
