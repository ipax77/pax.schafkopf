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
baseline infrastructure; live seat lifecycle, latency, and human playtests remain to be done.
