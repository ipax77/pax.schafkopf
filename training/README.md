# Bot baseline and evaluation

Milestones 1–4 of [the training plan](../docs/training-plan.md) are implemented:
WSL preflight, reusable C# heuristic policies, seeded paired evaluation, playable live
bot tables, and a versioned observation encoder. A bounded TensorFlow imitation sample
is available; PPO, training rollouts, and neural live serving remain pending.

## Start and watch sample training

From the repository root **inside WSL**, using the existing environment described below:

```bash
bash training/run-sample.sh
```

This performs the full GPU preflight, builds with the selected .NET SDK, generates 2,000
conservative-teacher games plus 256 held-out games, and trains separate bidding and playing
networks for up to three epochs. It then evaluates the best saved pair on 32 further deal
seeds, all four seats and dealers: 512 candidate games plus 512 paired reference games.
The sample is **supervised heuristic imitation**, the plan's warm-start stage. It is not PPO,
and the sample model is not installed into live tables.

The script prints its unique run directory under
`~/.local/share/pax-schafkopf/runs/`, plus stage, epoch, batch, percentage, loss, teacher
agreement, and estimated remaining batch time. Keep that terminal open. To watch from a
second WSL terminal, use the printed run directory:

```bash
bash training/watch-training.sh /path/printed/by/the/run
```

Ctrl+C in the viewer stops only the viewer. Ctrl+C in the training terminal interrupts the
run. Resume with the same data, weights, Adam state, and deterministic epoch shuffle:

```bash
bash training/run-sample.sh --resume /path/printed/by/the/run --epochs 3
```

`--epochs` is the **total target**, including completed epochs. Other settings on resume
come from `run.json`. An incomplete epoch is rerun from the latest completed checkpoint.
Partial data generation is preserved separately and regenerated. Concurrent writers to one
run are rejected. Completed checkpoints are committed atomically; every save reloads both
networks and checks predictions and optimizer state. No global shell configuration changes.

A larger explicitly sized run can be started with:

```bash
bash training/run-sample.sh --games 10000 --validation-games 1000 --epochs 3 --eval-seeds 128
```

Other options: `--output /new/run/directory`, `--batch-size 512`, `--seed 20261120`, and
`--gpu-memory-mb 2048`. The output directory must be new; resume existing runs explicitly.
`SCHAFKOPF_PYTHON` can select another prepared Python environment, which still must pass
the pinned full preflight. Default GPU allocation is capped at 2 GiB. The verified default
run used about 503 MiB of disk including data/build/checkpoints and finished in 22 seconds
on this machine; larger runs consume proportionally more data space. Store outputs on the
Linux filesystem. Repeated runs use new directories, not overwritten models.

Each run contains:

- `progress.log` and atomically updated `status.json` for live progress.
- `metrics.jsonl`: per-epoch held-out loss, accuracy excluding forced moves, forced fraction,
  and terminal-value error. Teacher agreement measures imitation, not game strength.
- `data/dataset.json`: encoder blocks, seed partitions, teacher version, row format, hashes;
  binary shards and replayable traces alongside it.
- `checkpoints/` and `latest.json`: resumable epoch snapshots, including best-so-far models.
- `models/best-bidding.keras`, `best-playing.keras`, and `pair.json`: the pair selected by
  validation loss, with action/rules/encoder metadata and `live_promoted: false`.
- `evaluation.json`: paired raw payouts with base-seed bootstrap intervals, contract counts,
  legality diagnostics, and every statistical seed block. Evaluation starts at validation
  index 1,000,000, disjoint from the demonstration validation range.
- `run.json` and `environment*.json`: settings, selected SDK/runtime, package versions, GPU,
  source commit, and dirty-state evidence.

Architecture follows the plan: two 128-unit ReLU layers for bidding, two 256-unit layers for
play, separate policy/value heads, masked cross-entropy plus 0.5 terminal-value MSE, Adam,
and clipped gradients. Forced moves train only the value head. Best models are selected by
held-out policy loss; stop early if neither improves for two epochs. Data are memory-mapped
in bounded batches. Both networks consume the [same private C# encoding](../docs/observation-encoding.md).
The sample evaluator uses batches over a local stdio bridge, with C# retaining all rules;
this is a sample transport, not the planned HTTP inference service or PPO collector.

Verification:

```bash
bash training/dotnet.sh test src/sk.tests/sk.tests.csproj -c Release \
  --artifacts-path /tmp/schafkopf-training-tests
"$HOME/.venvs/pax-schafkopf/bin/python" training/test_training.py
```

The 2026-11-20 sample completed all three epochs and 1,024 evaluation games with zero illegal
actions. Held-out choice accuracy rose from 63.3% to 77.1% (bidding) and 35.2% to 71.0% (play).
Paired payout was -2.27 raw units/deal, 95% seed-block interval [-3.44, -1.02], against the
teacher. It is a working training/checkpoint pipeline, **not an improvement over the live bot**.
See [recorded results](results/README.md).

## Live heuristic tables

Start the API and PWA as described in the root README. Create a table and select
**Freie Plätze mit Bots füllen**. Invite human players first for a table with one or two
bots. All live bots use the fixed, stateless `heuristic-v1/conservative` policy for bidding
and play; it cannot change during a round. No Python/model service is needed.

Bots are named, server-owned seats with no SignalR connection. Only connected human table
members can fill/remove them, and only before play or between rounds. Use the result dialog
to remove a bot before a friend joins. Seat cash balances persist across replacements;
the completed result retains its original players, hands, and payouts. Bots ready on
settlement, including all-pass. Every human must select **Weiter** for another deal;
changing the lineup clears human readiness.

`BotTurnScheduler` permits one outstanding policy call per table, computes from detached
private observations outside the game lock, and applies only current legal actions through
the revision guard. Broadcasts are serialized per table. Disconnects pause the table and
invalidate pending work; reconnecting resumes it. A late disconnect from an old connection
does not undo a successful rejoin. Leaving mid-deal reserves the human's hand for rejoin;
when the last connected human explicitly leaves, the table and join code are removed.
Tables with all humans disconnected are retained for 30 minutes, then cleaned up.

Optional API configuration under `Bots` uses .NET `TimeSpan` strings:

```json
{
  "Bots": {
    "TurnDelay": "00:00:00.900",
    "TrickDelay": "00:00:02.600",
    "PollInterval": "00:00:00.050",
    "DisconnectedTableRetention": "00:30:00"
  }
}
```

Defaults allow the existing card/trick animations to finish. The fixed policy itself runs
locally; display pacing is separate from inference latency. Neural deadlines, fallback,
and measured p95 serving gates remain part of the future neural adapter milestone.

Live regression tests exercise four consecutive played rounds with each of 1/2/3 bot seats,
bot-led auctions/declarations, all-pass readiness, private snapshots, bot identity protection,
replacement, duplicate/stale/illegal actions, in-flight disconnect/rejoin, cancellation,
abandoned-table cleanup, and independent progress at ten tables. Local browser verification
completed a human-declared Wenz and a bot-declared Ruf, including next-round startup and bot
replacement, with no browser console errors. These are mechanics checks, not strength claims.

## Environment

Run commands from the repository root inside WSL. `training/dotnet.sh` selects
`$HOME/.dotnet/dotnet`, checks SDK **10.0.404**, and sets `DOTNET_ROOT` and the child PATH.
It fails instead of falling back to the system SDK. Isolate Windows/WSL build artifacts:

```bash
bash training/dotnet.sh test src/sk.tests/sk.tests.csproj -c Release \
  --artifacts-path /tmp/schafkopf-training-tests
```

Create a dedicated Python 3.12 environment on the Linux filesystem. GPU package installation
follows [TensorFlow's WSL/pip instructions](https://www.tensorflow.org/install/pip), with pinned
versions. The lock records the complete environment verified on this machine:

```bash
python3 -m venv "$HOME/.venvs/pax-schafkopf"
"$HOME/.venvs/pax-schafkopf/bin/python" -m pip install \
  -r training/requirements-wsl-py312.lock
"$HOME/.venvs/pax-schafkopf/bin/python" training/check_environment.py \
  --output /tmp/schafkopf-environment.json
```

If Ubuntu's `ensurepip` package is unavailable but a recent global pip exists, this avoids
changing global Python packages (the verified setup used this route):

```bash
python3 -m venv --without-pip "$HOME/.venvs/pax-schafkopf"
python3 -m pip --python "$HOME/.venvs/pax-schafkopf/bin/python" install \
  -r training/requirements-wsl-py312.lock
```

The preflight writes a JSON manifest even on failure and exits nonzero if requirements fail.
It records selected SDK/runtime installations, Python/venv, dependency versions and `pip check`,
GPU/driver, source commit and dirty state. It requires TensorFlow 2.21.0, GPU discovery, and a
real GPU matrix multiplication with CPU fallback disabled. `--core-only` explicitly skips
TensorFlow/GPU checks; it is not evidence that the training environment is ready.

## Heuristic policy

`src/sk.bot` references only `sk.shared`. `HeuristicPolicy` implements `IGamePolicy` and accepts
only the acting seat's `GameObservation`; it has no access to a game instance or unseen hands.
It is stateless and safe to reuse. Ties always choose the lowest action ID.

- `balanced`: normal bidding thresholds are 6.0 Ruf, 8.5 Wenz, and 9.0 Solo, applied to the
  documented score in `HeuristicPolicy.BidScore` (trumps, high trumps, side aces, coverage/voids).
- `conservative`: adds 1.0 to those thresholds; card play is identical. Both styles pass if a
  current bid cannot be beaten and passing is legal, and handle forced declaration otherwise.
- Sie is preferred when legal. The only proactive Tout declaration in v1 is Wenz with all four
  Unter and an uninterrupted sequence from the ace in every held side suit. Other legal Tout
  contracts can still be evaluated through controlled auctions and played normally.
- Card play considers public trick strength, known partners, drawing trump, side winners,
  searching the called suit, feeding a secure teammate, cheap winners, and cheap discards.
  Partnership is never inferred from another seat's actual hand.

These are deliberately simple baselines. Change the policy version when changing decisions;
the fixed baselines must remain interpretable when later compared with trained models.

## Evaluate and replay

The evaluator needs no Python, TensorFlow, web server, or GPU:

```bash
bash training/dotnet.sh run --project src/sk.training -c Release \
  --artifacts-path /tmp/schafkopf-training-build -- evaluate \
  --output /tmp/balanced-vs-conservative.json --seeds 500 \
  --candidate balanced --reference conservative \
  --opponents conservative,conservative,conservative

bash training/dotnet.sh run --project src/sk.training -c Release \
  --artifacts-path /tmp/schafkopf-training-build -- replay /tmp/balanced-vs-conservative.json
```

For each base seed, the evaluator runs all four dealers and all four focal seats. It pairs a
candidate game with a reference game changing only the focal policy. Opponents are supplied in
clockwise order relative to the focal seat; policy sampling streams stay attached to physical
seats and are reset identically for each pair. **500 base seeds produce 16,000 games.**
Changing the dealer alone does not redistribute a seeded hand.

Reports include candidate/reference raw payout, paired improvement, 95% percentile bootstrap
intervals, the full per-seed blocks, contract/suit/role breakdowns, all-pass and interest counts,
Tout outcomes, policy/rules/seed versions, build SDK/runtime, elapsed time, and action checksum.
Bootstrap units are base-seed averages over all 16 paired seat/dealer combinations, never
individual actions or games. Contract/role breakdowns have descriptive totals, not independent
confidence intervals. Natural auctions can differ between the two sides of a pair.

`--split Training|Validation|Confirmation`, `--master`, and `--start` define the deal stream.
Within one master seed, the three splits occupy disjoint partitions of a bijective 64-bit map;
use the same master for the split guarantee. Use fresh confirmation indices after tuning against
an earlier confirmation set. Replaying a report checks its two saved example games. To reproduce
the entire comparison, rerun its recorded options and compare the checksum and seed blocks.

Optional modes:

- `--contract 3..16` runs the same forced legal auction for candidate and reference. The declarer
  rotates across base-seed indices, independently of dealer/focal-seat rotations. Ineligible
  seeds are skipped *before either game* and counted; at least two eligible base seeds are required.
  For example, `--contract 3` isolates Ruf Schellen play. Bidding is not evaluated in this mode.
  Randomly finding Sie is not a useful test strategy; engine fixtures cover that rare contract.
- `--traces /tmp/games.jsonl` streams every complete game trace; each pair is candidate then
  reference. Normal reports keep only two example traces and all per-seed aggregates.
- `--candidate random` or `--opponents random,random,random` uses a versioned uniform legal
  policy. Random bidding often declares losing Tout; beating it is only a smoke test.
- `--bootstrap` and `--bootstrap-seed` control reproducible confidence intervals. Use at least
  100 resamples; the default is 2,000. Small seed suites are debugging/validation, not promotion.

Illegal actions or episodes exceeding the engine's 40-action bound fail the run instead of
substituting random actions or dropping results. The CLI saves the accepted action prefix,
policy/seed metadata, and error in `<output>.failure.json`; `replay-failure` replays that prefix.
Successful games check zero-sum payout and terminal point/trick conservation.

## Verification and recorded results

See [results/README.md](results/README.md) for the environment, refreshed engine measurements,
and first validation comparisons. The new tests cover bidding/forced contracts, private Ruf
partnership, feeding/winning tricks, called-ace legality, mixed seeded games, replay, seed split
isolation, seat/dealer coverage, identical-policy comparisons, and block statistics.

The existing API/test dependencies emit NuGet audit warnings for Microsoft.OpenApi and
OpenTelemetry packages. The new bot/evaluator projects add no NuGet packages. These pre-existing
dependency updates are separate from the bot implementation.
