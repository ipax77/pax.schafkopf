# Bot baseline and evaluation

Milestones 1–4 of [the training plan](../docs/training-plan.md) are implemented:
WSL preflight, reusable C# heuristic policies, seeded paired evaluation, playable live
bot tables, and a versioned observation encoder. A bounded TensorFlow imitation sample
and warm-started PPO are available, including complete-game rollouts, controlled playing
curricula, timed sessions, and checkpoint resume. Frozen trained pairs can run locally in
the API on CPU for experimental human play.

## Gameplay-first experiment (2026-11-21)

The human playtest exposed weak card choices and teamwork despite an improved selection
score. The new `gameplay-playing` teacher translates [the gameplay notes](../docs/gameplay.md)
into explicit, testable card preferences while keeping conservative bidding. The original
`heuristic-v1` benchmarks stay unchanged. A 256-seed validation comparison found that the
first notes-based bidder regressed (-2.959 raw units/deal, 95% interval [-4.073, -1.841]);
the isolated card changes scored +0.244 [-0.198, +0.742], which is inconclusive. Consequently
the recommended preset uses exact **conservative bidding**, not the experimental bidder.
The encoder already includes every card in the current trick, public history, and known
partnerships; the new regression fixtures check that identical hands/lead cards with a
different current winner lead to different teaching decisions.

Start a fresh imitation run in WSL so the player learns the new teacher's examples:

```bash
bash training/run-sample.sh --teacher gameplay-playing --tactical-copies 4 \
  --games 30000 --validation-games 3000 --epochs 20 --eval-seeds 128 \
  --seed 20261121 --output "$HOME/.local/share/pax-schafkopf/runs/gameplay-imitation-20261121"

bash training/run-gameplay.sh "$HOME/.local/share/pax-schafkopf/runs/gameplay-imitation-20261121" \
  --seed 20261121 --minutes 90
```

Use a new output directory if that name already exists. `run-gameplay.sh` also accepts a
compatible PPO checkpoint directory or `pair.json` for a continuation experiment. It starts
a new run with fresh Adam state; existing runs keep their settings on `--resume`.

The experimental preset uses exact conservative C# bidding rules in **every seat**, 25% self-play and
75% one learner with three fixed players (conservative, balanced, or gameplay teacher).
40% of games use controlled contracts, retaining the 60/20/20 Ruf/Wenz/Solo mix and
80% own-hand-accepted / 20% unrestricted legal hands. The remainder use natural auctions.
Only card choices enter PPO. The learning rate is 0.0001, with 32,768 decisions/update,
128 concurrent games and the existing 2 GiB GPU cap. These are experiment settings, not
measured optimal hyperparameters. Historical neural opponents remain future work.

The optional `--bidder gameplay` / `--teacher gameplay` prototype interprets the bidding notes
as follows. It is retained for isolated experiments, not recommended as the main bidder:

- Ruf: at least five trumps and at most one side suit besides the called suit.
- Wenz: three side aces, with opening lead or at least two Unter; alternatively, at least
  two Unter and an ace-headed side suit of at least four cards. Opening lead is preferred.
- Solo: at least six trumps, at most one side suit without a ten, and one of the top two
  trumps; rear hand is preferred. This is a conservative proxy for limited losing tricks,
  not an exact estimate of what opponents can win.
- Sie and the original sufficient Wenz-Tout condition are retained; forced bids remain legal.

The `gameplay-playing` teacher uses these card-play preferences:

- On lead, the declaring side draws outstanding trumps. Defenders search for an unrevealed
  called ace and prefer fresh side suits. Once every suit has been led, repeats are allowed;
  the exhausted-trump exception permits cashing side winners.
- With a known teammate securely winning, avoid overtaking if a legal non-winning card can
  contribute at least as many points. Unknown Ruf partners are never guessed. An opponent
  still to act can invalidate the secure-win assumption. Other overtakes can be useful.

These preferences guide the imitation teacher and diagnostics. They do **not** alter legal
action masks, force the neural player's choices, or add bonuses/penalties to payout rewards.
`--tactical-copies 4` gives rare, clear partner-feeding demonstrations four times the sampling
weight in imitation training. Validation remains unweighted and split by deal, and PPO still
uses fresh on-policy decisions only. The dataset records the added row count explicitly.

Checkpoint selection uses 13 disjoint validation panels, 64 proposed seeds each by default:
three natural opponent styles; two tables with three neural seats and one fixed player;
all three legal Ruf suits, Wenz, and all four normal Solo suits. Auctions are identical
between candidate/reference controlled games. The fixed bidder is shared by every seat in
natural evaluations too. The score weights one-neural-seat natural games 25%, three-neural-seat
natural games 25%, and controlled Ruf/Wenz/Solo equally within the remaining 50%.
This is a curriculum score, not natural-game payout. Independent base-seed blocks are
bootstrapped within each panel; ineligible Ruf seeds are reported and skipped in both arms.
Every 20 updates, and at startup/shutdown, the full suite runs. Selection also requires each
contract family's lower 95% bound of gain over the initial player to exceed -1 raw unit/deal.
Small suites can therefore retain the initial pair. This is a validation guard, not a public
promotion test; use fresh confirmation seeds and human play before strength claims.

Each panel records `by_contract_role` and `gameplay_guidance`: opportunities, preferred choices,
actual overtakes of a secure known partner, and bounded private-observation examples for review.
Guidance disagreements are not automatically mistakes. Inspect the three-neural-seat panels
and Wenz results instead of relying on the pooled score alone.

To audit either existing native export without starting training:

```bash
bash training/dotnet.sh run --project src/sk.training -c Release \
  --artifacts-path /tmp/schafkopf-gameplay-audit -- \
  audit-gameplay models/serious-20261121-update0450/pair.json /tmp/gameplay-audit.json 64
```

`play-trained.sh` and `export_policy.py` preserve the fixed bidder in `pair.json` as
`bidding_policy: heuristic-v1/conservative` (or `gameplay-v1` for the experimental bidder).
The live API uses that exact bidder with the neural player;
it does not quietly substitute the imitation bidder. Old exports retain neural bidding.

Only begin a separate `--train bidding --bidder neural` experiment after isolated play
evaluations and playtests are satisfactory. Freeze the selected player, use natural deals,
and start with `--self-play-percent 0` to keep opposing bidders fixed. The frozen playing
companion uses argmax in new runs; legacy resumes retain their original sampling behavior.
Do not automatically switch to bidding after a short number of playing updates.

## Timed training and trained-bot play

The completed local run's selected pair is ready at `models/serious-20261120/`:

```bash
bash training/play-trained.sh models/serious-20261120
```

See the [measured results](results/README.md#timed-trained-bot-run) for its 8.69 million
PPO decisions, held-out improvements, and remaining weaknesses. Model binaries stay local
and are excluded from Git; the reproducible settings and evaluation summaries are tracked.

From the repository root **inside WSL**, prepare a substantial imitation warm start (the
three-epoch sample is primarily a pipeline check):

```bash
bash training/run-sample.sh --games 30000 --validation-games 3000 --epochs 20 --eval-seeds 500
```

Then start the 90-minute PPO preset with that run directory:

```bash
bash training/run-serious.sh /path/to/larger/imitation/run
```

Add `--minutes 60` or `--minutes 120` to change the session budget. The runner finishes its
current complete rollout/update and final evaluation when time expires; this can exceed
the requested time by an update/evaluation. Keep the training terminal open. The preset uses:

- 32,768 playing decisions/update, 8,192 bidding decisions/update, 128 concurrent games.
- Playing PPO with the imitation bidder held fixed. The first staged pilot collapsed toward
  always passing, so bidding PPO is opt-in (`--train staged`, `--train bidding`, or `--train alternate`).
- Controlled play: 60% Ruf, 20% Wenz, 20% Solo; 80% conservative-accepted declarer hands,
  20% unrestricted legal hands. Acceptance sees only that declarer's own observation. The
  engine executes scripted legal auctions, which never enter PPO. Rejected seeds are counted
  and consumed, with a 10,000-attempt cap. The default mix is 20% controlled / 80% natural play.
- Half self-play, half one learner with three conservative heuristics; no historical pool yet.
- A fixed 500-seed validation panel at startup, every ten updates, and shutdown. `best.json`
  retains the highest mean payout pair, including the initial pair if training does not improve it.
- Four recent checkpoints plus initial/best, and two recent large trajectory arrays. All
  accepted game traces, per-update metrics, and seed/RNG metadata remain available.
- A 2 GiB TensorFlow allocator cap. The live app can run independently on CPU.

Watch from another WSL terminal using the run directory printed at startup:

```bash
bash training/watch-training.sh /path/to/run
```

Resume for another bounded session (the update target includes completed updates):

```bash
bash training/run-sample.sh ppo --resume /path/to/run --updates 100000 --minutes 90
```

The last session's time budget is inherited if `--minutes` is omitted on resume; explicit
`--minutes 0` disables the timer. Other training settings remain pinned in `run.json`.
Current checkpoints and the best validated pair can differ substantially. A longer run does
not guarantee a stronger bot; inspect the recorded evaluations before making strength claims.
For an explicit staged experiment, `--train staged` uses 24 initial controlled playing updates,
then four playing updates per bidding update; its bidding updates always use natural deals.
Inspect actual interest/declaration rates, not just aggregate payout or imitation accuracy.

To play against the best saved pair:

```bash
bash training/play-trained.sh /path/to/run
```

This exports a new immutable copy, builds and starts the API/PWA, and prints their logs.
Open **http://localhost:5027**, create a table, and select **Freie Plätze mit Bots füllen**.
You get one human seat and three trained bots. Keep this terminal open; Ctrl+C stops these
servers and leaves training alone. Existing servers on ports 5283/5027 must be stopped first.
You can also pass an existing exported model directory. Opening a run while training is active
uses the best pair available at launch; later checkpoints never change an existing game.

`http://localhost:5283/bots/policy` reports the loaded pair ID and fallback count. The API loads
one pair at startup through `Bots:ModelManifest` (`Bots__ModelManifest` as an environment
variable). Without that setting it uses the conservative heuristic. Invalid manifests fail
startup clearly; a runtime inference failure logs and falls back to the legal heuristic.

For an API started manually, export first:

```bash
"$HOME/.venvs/pax-schafkopf/bin/python" training/export_policy.py /path/to/run \
  --output /new/export/directory
Bots__ModelManifest=/new/export/directory/pair.json bash training/dotnet.sh run --project src/sk.api
```

The `dense-f32-v1` export stores the existing two ReLU layers and linear policy/value heads as
SHA-256-checked float32 files, with encoder/rules/action-selection metadata. `NeuralPolicy`
implements only this fixed feed-forward calculation; C# still supplies observations and legal
actions. It needs no Python/GPU/network service during play. Exported pairs use masked argmax,
and are marked experimental. TensorFlow/C# output and action parity are checked with
`training/verify_export.py`; native pairs can also be evaluated by the existing CLI using
`--candidate neural:/absolute/path/to/export/pair.json`.

For the predeclared held-out diagnostics after training finishes:

```bash
"$HOME/.venvs/pax-schafkopf/bin/python" training/finish_run.py /path/to/run \
  --output /new/report-and-model-directory --confirmation-start 3000000
```

This freezes the selected pair before evaluating it, exports initial/best CPU models, checks
TensorFlow/native parity and one/ten-worker inference latency, then evaluates the fixed
[protocol](results/serious-20261120-protocol.json): 2,000 fresh confirmation seeds against each
heuristic style and 500 controlled seeds each for Ruf Schellen, Wenz, and Solo Schellen. It
compares against the original imitation pair, saves all seed blocks and role/contract diagnostics,
and writes `playable.json` into the run. This is an experimental diagnostic suite, smaller than
the public-release gate. `--quick` uses a separate validation panel solely to test this workflow.
`--confirmation-start` must identify a previously unused Confirmation range; the example
3,000,000 is suitable only if you have not used it before. The original 2,000,000 panel has
already informed development and must not be reused as fresh confirmation. Validation and
Confirmation are separate partitions even when their numeric indices happen to match.

## PPO from imitation checkpoints

From the repository root **inside WSL**, start with the existing imitation run:

```bash
bash training/run-sample.sh ppo \
  --warm-start "$HOME/.local/share/pax-schafkopf/runs/sample-20261120-202018-24475a"
```

`--warm-start` accepts an imitation run directory, its `models/` directory, or a compatible
`pair.json`. Both policy/value networks retain their learned weights. The source files are
copied into the new run and never overwritten; PPO starts with fresh Adam moments. Encoder,
rules, input/output shapes, and reward scale must match.

The default is a bounded **10-update playing run**: 4,096 learner card decisions per rollout,
64 simultaneous C# games, up to four PPO epochs, and a frozen imitation bidder. Every active
game finishes before an update, so actual decision counts can exceed the target. The run
evaluates the initial pair, every fifth update, and the final pair on 32 validation seeds
across all seats/dealers. These are small diagnostic evaluations, not promotion gates.

Progress prints the rollout percentage, games, decisions/second, draining status, epoch,
KL, clip fraction, entropy, value error, evaluations, and checkpoint commits. The script prints
a unique run directory under `~/.local/share/pax-schafkopf/runs/`. In another WSL terminal:

```bash
bash training/watch-training.sh /path/printed/by/the/ppo/run
```

Ctrl+C in the viewer stops only the viewer. Ctrl+C in the training terminal interrupts the
run. To resume or extend it:

```bash
bash training/run-sample.sh ppo --resume /path/printed/by/the/ppo/run --updates 20
```

`--updates` is the **total target**, including completed updates. All other training settings
on resume come from `run.json`. Resume restores both models, Adam tensors/iterations, the
NumPy sampling/shuffle RNG, completed decision counts, and the next unused training deal index.
An interrupted collection or update is rerun from the last committed checkpoint; its partial
files remain available for inspection. A lock rejects concurrent writers. Checkpoints are
immutable directories published through an atomic `latest.json` pointer, and every save
reloads both networks to verify predictions and optimizer tensors. GPU arithmetic can differ
slightly across executions; saved action traces replay deterministically in C#.

Choose another mode when starting a new run:

| Option | Updated policy | Frozen policy |
| --- | --- | --- |
| `--train playing` (default) | Playing | Bidding |
| `--train bidding` | Bidding | Same neural player in every seat |
| `--train alternate` | Playing on odd updates, bidding on even updates | The other network for the entire collection/update |
| `--train staged` | Controlled playing warmup, then four playing / one bidding update | The other network for the entire collection/update |

For example, a larger explicitly bounded playing run uses:

```bash
bash training/run-sample.sh ppo --warm-start /path/to/imitation/run \
  --updates 100 --decisions 65536 --pool 128 --eval-seeds 128
```

Other options include `--epochs 4`, `--batch-size 2048` (default automatic: 2,048 playing,
1,024 bidding), `--eval-every 5`, `--seed 20261120`, `--start-index 2000000`,
`--gpu-memory-mb 2048`, and `--output /new/run/directory`. Keep outputs on the Linux filesystem.
The same pinned GPU preflight and selected .NET SDK apply to imitation and PPO.

The collector uses natural deals with a deterministic mix of half self-play and half one
learner against three conservative heuristics, rotating dealers and focal seats. For bidding
updates, all seats use the same frozen neural player. Only selected learner decisions enter
the loss. This PPO runner uses batched local stdio transport. Controlled play is available in
the timed preset above or with `--controlled-percent`. Historical opponent pools and
strength/scratch comparisons remain pending. Exporting and launching a pair for local play is explicit.

Each PPO run contains:

- `progress.log` and `status.json`: live human-readable and machine-readable progress.
- `warm-start/`: source models, source pair manifest, and hashes recorded in `run.json`.
- `rollouts/update-*/games.jsonl`: complete accepted action traces and four-seat payouts;
  `trajectories.npz`: selected learner features/masks/actions, old log probabilities/values,
  per-seat terminal rewards, returns, and advantages; `rollout.json`: both policy versions,
  seeds/RNG state, contract counts, timings, and row counts; `ppo.json`: epoch/phase metrics.
- `metrics.jsonl`: completed update metrics, including approximate KL, clipping, entropy,
  forced fraction, gradient norm, value MSE, and explained variance.
- `checkpoints/update-*/`: resumable `.keras` files, state, and versioned `pair.json`;
  `latest.json` points to the last committed pair; `best.json` points to the pair with the
  highest mean payout on the fixed validation panel.
- `baseline-evaluation.json`, `evaluation.json`, and `evaluations/`: raw-payout evaluation
  against three conservative opponents, with base-seed bootstrap intervals; paired change
  from the warm-start pair uses the same validation seed panel. Bidding-only results evaluate
  the whole pair and are not a controlled isolation of bidding strength.
- `run.json` and `environment-*.json`: hyperparameters, source/dependency versions, and GPU.

PPO masks sampling and loss identically, samples at temperature 1, and applies the clipped
surrogate with clip 0.2, value coefficient 0.5, entropy coefficient 0.01, and global gradient
norm limit 0.5. Adam uses learning rates 0.0003 (play) / 0.0001 (bid), epsilon 0.00001.
Remaining epochs stop when full-rollout approximate KL exceeds 0.03. Returns use each seat's
terminal payout / 100, gamma = lambda = 1, without reward clipping. Forced moves train value
only; advantage normalization excludes them. Old statistics remain fixed during all epochs,
and the next update collects fresh data. Invalid/stale actions, nonfinite outputs/losses,
unfinished trajectories, and exhausted game caps fail the run and retain the last checkpoint.

Replay a completed rollout through the C# engine, including checking its saved payouts:

```bash
bash training/dotnet.sh run --project src/sk.training -c Release \
  --artifacts-path /tmp/schafkopf-training-build -- replay-rollout /path/to/rollout/games.jsonl
```

After a failed collector, use `replay-rollout-failure` with its `games.jsonl.failure.json`
to replay accepted prefixes without requiring terminal states. Sampling uses the run's NumPy
RNG, whose states are saved in `rollout.json`; the generic trace's `sampling_seed` is unused.

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
this transport also supports the PPO collector above. The planned HTTP service remains pending.

Verification:

```bash
bash training/dotnet.sh test src/sk.tests/sk.tests.csproj -c Release \
  --artifacts-path /tmp/schafkopf-training-tests
"$HOME/.venvs/pax-schafkopf/bin/python" -m unittest discover -s training -p 'test_*.py'
```

The 2026-11-20 sample completed all three epochs and 1,024 evaluation games with zero illegal
actions. Held-out choice accuracy rose from 63.3% to 77.1% (bidding) and 35.2% to 71.0% (play).
Paired payout was -2.27 raw units/deal, 95% seed-block interval [-3.44, -1.02], against the
teacher. It is a working training/checkpoint pipeline, **not an improvement over the live bot**.
See [recorded results](results/README.md).

## Live heuristic tables

Start the API and PWA as described in the root README. Create a table and select
**Freie Plätze mit Bots füllen**. Invite human players first for a table with one or two
bots. By default live bots use the fixed, stateless `heuristic-v1/conservative` policy for
bidding and play. The trained-play launcher above configures an immutable neural pair instead.
Neither path needs a Python/model service during play.

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
locally; display pacing is separate from inference latency. Native neural CPU inference and
runtime fallback are available; the full live-load latency and reliability soak gates remain pending.

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
