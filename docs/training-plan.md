# WSL training and live bot plan

Status: milestones 1–3 implemented and verified; encoding and neural training remain pending.
Updated: 2026-11-20.

This is the implementation plan following the engine redesign and performance work.
[engine.md](engine.md) defines the authoritative rules and action IDs.
[tensorflow.md](tensorflow.md) is the original architecture proposal; where it differs,
this plan takes precedence. No TensorFlow dependency belongs in `sk.shared`.

## 1. Objective and decisions

Train bots that can participate in both bidding and card play, then use the same policies
to fill seats at live tables. Preserve one C# rules implementation for training and live games.

The first target is a reliable opponent for casual human games: sensible bidding, competent
card play with unfamiliar partners, and responsive turns. Expert strength is a later measured
goal. Deliver a playable heuristic bot first, then replace its policies with trained ones only
when evaluation demonstrates an improvement. Simulation throughput alone is not the objective.

The repository now has `HeadlessGame`, private observations, `IGamePolicy`, guarded live-action
hooks, heuristic policies, a seeded evaluation harness, and WSL preflight checks. See the
[implementation guide and recorded results](../training/README.md). The bot seat lifecycle
and scheduler are implemented. The encoder and trainer remain pending.

| Area | Decision |
| --- | --- |
| Simulator | Existing .NET 10 `HeadlessGame`, running in WSL |
| Learning | Python/TensorFlow, initially synchronous PPO |
| Policies | Separate bidding and playing models, each with its own value head |
| Seats | Share each model across all four seats; encode positions relative to the acting seat |
| Transport | Batched local HTTP for the first implementation |
| Scheduling | Separate bidding and playing queues, served by one Python process |
| Rollouts | Complete games; learner and opponent versions fixed during collection |
| Reward | Acting seat's terminal payout divided by 100; no reward clipping |
| First playable bot | C# heuristic bidder/player, also used as a benchmark and timeout fallback |
| Initial learning | Brief heuristic imitation, then play PPO, then bidding against frozen play |
| Subsequent learning | Alternate training phases, freezing the other model |
| Training tables | Current learner, fixed heuristics, and a small pool of frozen policy pairs |
| Promotion | Held-out paired payout comparisons, contract diagnostics, and human playtests |
| Deployment | Policy adapters through existing live service hooks; ONNX is a later option |

The current single-pass, two-stage auction remains unchanged. Rule variants and a redesigned
auction are separate work and would require versioned training data.

Numbers introduced below are initial experiment settings and acceptance targets, not measured
optima or promises of strength on a particular training budget.

## 2. WSL environment and executable selection

Use **`$HOME/.dotnet/dotnet`**, not the `dotnet` found on the default PATH.
The user reports these distinct installations:

| Command | SDK version |
| --- | --- |
| `dotnet --version` | 10.0.115 |
| `~/.dotnet/dotnet --version` | 10.0.404 — selected for this work |

Examples, run inside WSL from the repository root:

```bash
"$HOME/.dotnet/dotnet" --version
"$HOME/.dotnet/dotnet" --info
"$HOME/.dotnet/dotnet" test src/sk.tests/sk.tests.csproj -c Release \
  --artifacts-path /tmp/schafkopf-training-tests
"$HOME/.dotnet/dotnet" run --project src/sk.benchmarks -c Release \
  --artifacts-path /tmp/schafkopf-training-bench -- /tmp/schafkopf-bench.json
```

All future WSL scripts must resolve this executable explicitly and fail clearly if it is
missing. Do not silently fall back to the system installation. For tools that launch a
child `dotnet`, set `DOTNET_ROOT="$HOME/.dotnet"` and prepend `"$HOME/.dotnet"` to that
process's PATH. Do not overwrite HOME or modify global shell configuration.
Check the resolved SDK and runtime from the repository working directory before a run;
record both in its manifest. If SDK pinning is introduced, target 10.0.404 deliberately.

The previous engine measurements used the system SDK and reported runtime .NET 10.0.15.
Re-establish the baseline with the selected installation before comparing training throughput.
An SDK version is not a runtime version.

Previously verified Python/GPU setup: Python 3.12.3, TensorFlow 2.21.0, RTX 3080 Ti with
12 GiB VRAM, and successful TensorFlow GPU discovery. Verify again during environment setup.
Use a dedicated virtual environment with pinned dependencies. Keep Python, the simulator,
and policy server inside WSL; prefer the Linux filesystem for active training outputs.
Use isolated .NET build outputs when Windows and WSL share the checkout.

## 3. Two model contracts

### Bidding model

- Covers both interest decisions and contract declaration; phase distinguishes them.
- Produces 17 logits corresponding to engine action IDs **0..16**.
- Uses the applicable slice of the engine legal mask; unavailable actions cannot be sampled.
- Its value head predicts that seat's eventual scaled payout, including the outcome of play.
- Interest and declaration decisions belong to the same seat's bidding trajectory.
  Depending on the auction, a seat makes one or two such decisions.

### Playing model

- Produces 32 logits corresponding to card IDs **0..31**.
- Converts a selected card ID to engine action ID **17 + cardId**.
- Uses engine mask entries **17..48**; the model never decides legality itself.
- Its value head predicts the acting seat's eventual scaled payout.
- Each seat makes eight play decisions in a completed played game; all-pass games have none.

Separate checkpoints, optimizers, normalization settings, and hyperparameters are allowed.
An exported bot release identifies a tested **pair** of models, not just one checkpoint.

## 4. Observation encoding

Implement a versioned encoder over `GameObservation`. Keep it in C# so training workers and
live .NET bots use identical encoding. Python receives encoded tensors, not a second rules engine.
Define dimensions from the schema rather than adopting the proposal's placeholder size of 256.

Common features:

- Own current and original hand as card-presence features; phase and contract, including Tout.
  Reconstruct the original hand from current cards plus this seat's public plays, not results DTOs.
- Dealer, leader, declarer, current player, and public partner relative to the observing seat.
- Hand sizes, captured points, and trick counts in the same relative seat order.
- Current trick, distinguishing absent cards from actual card ID zero.
- Public interest/declaration history and ordered played-card history, with actor positions.
- Ran-away state and explicit unknown/absent indicators.

Add inexpensive derived features to the first encoder: played-card presence by seat, own remaining
card count by suit/trump, and own known role (declarer, called-ace holder, defender, or unknown).
Only mark a role when it follows from this seat's information. In Ruf, a non-declarer who did
not hold the called ace knows they are a defender, but may not know the other defender's seat.
Use the shared card/trump helpers; never derive features from the simulator's hidden hands.

Use fixed padded slots with validity masks for the bounded public history: up to eight bidding
actions and 32 card plays. Start with feed-forward models; recurrent models are a later experiment.
Use categorical/card encodings rather than treating raw card IDs as ordinal scalar values.
Document which features each model uses and freeze ordering in golden encoding fixtures.
Scale counts by fixed bounds (cards/tricks by 8, points by 120), not changing running statistics.
Use a small MLP first: two 128-unit hidden layers for bidding and two 256-unit layers for play,
with ReLU activations and separate linear policy/value heads. No dropout or batch normalization.
Benchmark a larger or sequence model only after learning and evaluation work end to end.

Never supply opponent hands, terminal starting-hand reveals, or an unrevealed partner assignment.
An ace holder may infer partnership from their own hand. Use `Observe`, not the UI results DTO.
Legality comes from the engine mask, which must also be recorded with the observation.
Privacy fixtures must compare indistinguishable histories with different unseen hands and
require identical encodings. Do not pass deal seeds, episode IDs, policy identities, or terminal
labels into either model. Those are collector metadata only. Arbitrary suit permutations are
not valid augmentation: hearts and the Ober/Unter suit order have special rule meanings.

## 5. Game pool and inference protocol

Maintain many independent `HeadlessGame` instances. Each instance has one owner. Start with
one simulation loop over the pool; thousands of games do not require thousands of threads.
Introduce bounded worker partitions only if profiling shows that simulation limits throughput.

At each iteration:

1. Collect pending observations and masks into bidding or playing queues.
2. Flush a queue when it reaches its batch limit or no more useful work is available.
3. Request actions, masked log probabilities, and value estimates from the matching model.
4. Validate response sizes, IDs, versions, finite numbers, and action legality before applying.
5. Apply each action once, record the transition, and route the next decision to its phase queue.
6. On completion, deliver all four terminal rewards to their trajectories and reset as appropriate.

Never wait indefinitely for a full bidding batch: bidding queues are naturally smaller and
less frequent. Keep partial batches supported. One outstanding batch per queue is sufficient
initially; explicit decision IDs make duplicate or stale replies detectable.

Proposed endpoints: `/act/bidding` and `/act/playing`, plus health/model metadata.
Requests identify batch, game episode, decision, rules version, encoder version, model version,
observations, and masks. Responses echo identifiers and include actions, selected masked log
probabilities, and values. Use model-local action IDs on the wire, with explicit adapters above.
With frozen opponents, partition queues by `(model kind, model version)` and record the seat's
policy pair. Never accidentally serve a historical-policy request with the learner's weights.
Keep weights immutable during collection and separate live serving from training updates.

Reject all-false masks rather than sampling arbitrary actions. Apply the identical mask when
computing PPO losses and entropy. Do not silently replace an invalid model action with a random
one and then train on its original log probability. Retries must not apply a transition twice.

No per-card HTTP requests and no SignalR in the training loop. Live inference may use small
batches and a short wait limit; it must not wait for a training-sized batch.
Start with 256 active games and batches up to 256; sweep upward only after a full rollout/update
works. Include batch sizes 1, 4, and 16 for live latency and 64, 256, 1,024, and 4,096 for training.
Keep rollout buffers in host memory and transfer minibatches to the GPU. Record peak host/VRAM
use, including resident historical models, before increasing the pool on the 12 GiB GPU.

## 6. Trajectories, rewards, and PPO updates

Record episode ID, seat, model kind, observation, legal mask, action, old masked log probability,
value estimate, reward, terminal flag, decision index, and **both** model versions. Record rules,
encoder, and reward-scale versions in the dataset/run metadata.
Also record the four seat-to-policy assignments, curriculum bucket, deal seed, action-sampling
seed, and whether each transition belongs to the current learner. Metadata is not policy input.

Group transitions by `(episode, seat, model kind)`. Successive actions by different players
are not adjacent steps in one agent's PPO sequence. For each model's sequence, zero intermediate
reward leads to that seat's terminal payout; a bidding sequence ends only when the game ends.
Both models optimize the same outcome through their respective decisions. Do not concatenate
their independent reward streams and accidentally count terminal payout twice in one return.

All four players receive their payout on termination, even though only one made the last move.
All-pass games produce valid zero-reward bidding trajectories and no playing trajectories.
This matters when a player has passed or made their final card decision before the game finishes.

At settlement, put `payout / 100` on the last recorded transition of **each** seat/model sequence
and mark it terminal, with zero bootstrap value. Earlier rewards are zero. This scaling preserves
the tariff objective, including the solo declarer's threefold exposure. Do not replace payout
with win/loss, clip rare large returns, reward individual trick points, or penalize passing.
Such changes would optimize a different game. Keep raw tariff units in evaluation reports.

Initially use complete episodes, gamma 1.0, and GAE lambda 1.0. These short sequences (at most
eight play or two bidding decisions) allow a simple Monte Carlo baseline; compare lambda 0.95
later if variance is a problem. For example, a seat paid +60 has return +0.6 at every recorded
decision of either model at lambda 1.0. A seat that declined interest must still get its final
outcome, and an all-pass sequence must return zero. Include both cases in hand-calculated tests.

Initial PPO settings:

| Setting | Playing | Bidding |
| --- | --- | --- |
| Learner decisions per collection, before draining active games | 65,536 | 16,384 |
| Minibatch size | 2,048 | 1,024 |
| Maximum epochs per collection | 4 | 4 |
| Adam learning rate / epsilon | 0.0003 / 0.00001 | 0.0001 / 0.00001 |
| PPO clip range / value-loss coefficient | 0.2 / 0.5 | 0.2 / 0.5 |
| Entropy coefficient / gradient norm limit | 0.01 / 0.5 | 0.01 / 0.5 |
| Target approximate KL | 0.02 | 0.02 |

Normalize advantages once over the rollout's learner decisions with more than one legal action.
Use those decisions for policy loss, entropy, and KL; retain forced moves in sequences and value
training. Report their fraction separately so forced endgame cards do not disguise policy collapse.
If a minibatch has no such decisions, skip its policy/entropy/KL terms rather than divide by zero.
Keep returns/value targets on the fixed payout scale. Freeze old log probabilities, old values,
advantages, and targets throughout the update; use only fresh learner rollouts for the next update.
Stop remaining epochs if mean approximate KL exceeds 1.5 times the target. Abort on nonfinite
losses/gradients and retain the last valid checkpoint. Record KL, clip fraction, legal-action
entropy by phase, gradient norm, value error, and explained variance (undefined for zero variance).

PPO uses repeated minibatch updates on freshly collected experience; it does not turn arbitrary
historical demonstrations into on-policy data. See the [PPO paper](https://arxiv.org/abs/1707.06347).
Mask both sampling and the loss distribution consistently, as supported by the
[invalid-action masking analysis](https://arxiv.org/abs/2006.14171).

Size rollout targets in each model's own learner decisions; bidding supplies far fewer samples.
Finish active games before updating a model, even if a nominal decision-count target is exceeded.
This avoids truncated-episode bootstrapping in the initial implementation.
Stop launching replacement games once the target is met, then drain the pool; otherwise a pool
that resets every finished game can keep collection open forever. Bound retries and fail a run
with a replayable trace on a stuck or invalid episode instead of discarding inconvenient losses.

Freeze the learner pair and all opponent versions throughout a collection period. Train only the selected model;
then publish a new pair manifest at a rollout boundary. Train only on transitions generated by
the learner policy for that update; heuristic and historical seats still play complete games.
No exploration override or heuristic replacement may reuse the network's original log probability.

## 7. Training stages and evaluation

### A. Build the playable baseline and evaluator first

Implement a deterministic, versioned C# heuristic pair through `IGamePolicy`, using only
`GameObservation` and engine-provided legal actions. It serves four purposes: an immediately
playable bot, a meaningful fixed opponent, an imitation teacher, and a live timeout fallback.

- Bidding: score legal contracts using own trump strength/length, side aces, and suit coverage;
  express interest only when a contract passes a documented threshold. Pass when permitted if
  no bid is attractive; handle forced declaration explicitly. Prefer Sie when available. Start
  conservatively on Tout, requiring a documented sufficient winning-hand condition.
- Play: distinguish leading, following, discarding, and trumping; consider current trick winner,
  card points, and known partnership. Use cheap winning cards when appropriate, feed points to
  a known winning teammate, and avoid wasting high cards on already lost tricks. These are
  heuristics to benchmark, not guarantees of optimal play.
- Pin tie-breaking and thresholds. Add a second frozen bidding style (more conservative or
  more aggressive) to detect overfitting to one baseline. Include a uniform legal random policy
  only as a smoke test; random declarations can create very costly, unrealistic Tout games.

Create the evaluation harness now, before PPO, and wire the heuristic into a human table using
section 9. Validate mechanics and collect playtest feedback while learning infrastructure is built.
The heuristic is a starting benchmark, not evidence of human-level strength.

### B. Warm-start the models

Generate an initial 100,000 seeded heuristic games, retaining each acting seat's private
observation, legal action, and terminal payout. Split by deal seed, not individual transitions.
Write demonstrations in bounded shards and stream training minibatches; do not retain millions
of expanded history tensors in RAM. Compact seed/action traces can reconstruct observations
through the versioned engine, but seeds and other seats' private data never enter model inputs.
Include separate controlled-contract examples for play coverage. Use masked supervised
cross-entropy to initialize each policy and terminal scaled payout to initialize its value head.
Use one to three passes with held-out imitation loss for early stopping; compare the resulting
bot's payout against its teacher before selecting an initialization. Forced actions are not
meaningful imitation-accuracy evidence. Keep the demonstration budget bounded.

This is a proposed head start to reduce random bidding and play, not a permanent imitation
constraint. Start PPO with new games sampled by the initialized learner. Do not put teacher
actions into PPO buffers or preserve a teacher loss indefinitely. A small from-scratch control
run should check whether imitation actually improves strength per wall-clock hour.

### C. Establish card play

Freeze the heuristic bidder and train only play. Start the controlled-contract sampler at
60% Ruf, 20% Wenz, and 20% Solo, balancing legal suits, declarer positions, and dealer seats.
Within each bucket, initially use 80% hands accepted by the heuristic's contract threshold and
20% unrestricted legal hands. This includes difficult positions without making hopeless
random declarations the whole curriculum. Log accepted/rejected deal counts and realized mix.

Construct contracts through validated auction actions: the selected declarer expresses interest,
the others decline, and that declarer chooses the legal target contract. If unavailable, resample
within the training seed stream, with an attempt cap that reports an unsatisfiable curriculum.
Selection may inspect that declarer's own observation only;
do not select using opponents' hidden hands. Scripted bidding decisions are not PPO data.

Once normal-contract evaluation improves, introduce 5% Tout (55/20/20/5 Ruf/Wenz/Solo/Tout),
with strong and weak declarer hands and both Wenz/Solo variants. Dedicated deterministic seeds
cover rare Sie; do not wait for random collection to encounter it or oversample it heavily.
The full engine mask remains authoritative throughout. Keep curriculum evaluations distinct
from natural-deal release results; oversampled Tout must not dominate the strength score.

### D. Learn bidding

Freeze a selected playing checkpoint. Train bidding on natural, unfiltered seeded deals and
full-game payouts, including all-pass. For the initial bidding experiment use that same frozen
playing model in every seat, varying only bidders. Evaluate candidate and reference bidding
with the same player so improvements can be attributed to bidding. Then test the full pair
against opponents with different playing policies before promotion.

Log interest rate, declaration/pass rates, contract frequency, and payout conditional on own
declarations. Inspect both excessive passing and reckless Solo/Tout; do not force an arbitrary
bid frequency or remove difficult deals. Use the heuristic initialization and fixed opposing
bidders to keep the opening experiment informative. An all-pass-only learner is not a success
merely because its loss is small or it avoids losses against copies of itself.

### E. Alternate improvements with varied tables

Freeze bidding while improving play, then freeze play while improving bidding. During later
play phases use 80% natural games from frozen bidders and 20% controlled contracts. During
bidding phases always use natural deals. Freeze every policy and table assignment for a whole
episode, and change the collection mixture only at rollout boundaries.

Start with this mixture of episodes, independently of the contract curriculum:

| Share | Table composition | PPO data |
| --- | --- | --- |
| 50% | Four seats using the current learner pair | All four seats of the model being trained |
| 25% | One learner seat, three fixed heuristic seats | Learner seat only |
| 25% | One learner seat, three seats drawn from frozen pair snapshots | Learner seat only |

Rotate learner seats uniformly. During isolated bidding training, override every seat's play
policy with the same frozen player as specified above. Before historical models exist, fill
that bucket with fixed heuristics. Keep at most four historical pairs initially: the initial
usable pair, current champion, and two spaced recent snapshots. Versions remain immutable;
update membership only between collections. This needs a small sampler, not a league service.

Other seats can be partners as well as opponents: especially in Ruf, assigning two bot versions
to two seats each does not create two fixed teams. Include independently trained models as they
become available. Work on [coordination with unfamiliar partners](https://arxiv.org/abs/2003.02979)
shows that self-play conventions may fail with new partners; applying that concern to Schafkopf
motivates these mixed tables and human playtests, not a claim that the same algorithm is optimal.

### F. Evaluate strength and promote a tested pair

Keep disjoint seed streams for training/demonstrations, tuning validation, and final confirmation.
Derive them from a recorded split scheme and check for overlap. Once final confirmation results
inform another change, retire that seed set to validation and use a fresh confirmation set.

Use three complementary suites:

1. **Natural games:** candidate pair versus fixed heuristic styles and frozen champions. For each
   deal/dealer/focal seat/opponent lineup, run the candidate in the focal seat and a paired game
   replacing only that seat with the incumbent. Keep other policies and sampling seeds fixed.
   Measure the focal seat's raw payout difference, not the total table payout. Also measure the
   candidate's absolute payout at tables with three identical heuristic seats.
2. **Controlled contracts:** identical legal auction traces with candidate/reference play swapped
   at the focal seat. Separate Ruf caller/ace holder/defender and Solo/Wenz declarer/defender,
   normal/Tout, suits, and seats. This isolates play from improvements caused by changing bids.
   For bidding-only comparisons hold all playing policies fixed instead.
3. **Mixed tables and human play:** test one, two, and three bot seats; include unfamiliar partners
   and older policies. Run at least two human sessions of 20+ completed deals before calling a
   release suitable for casual play. Record suspicious decisions as observation/action replays
   for analysis; player feedback complements, but does not statistically prove, playing strength.

For promotion suites, use all four focal seats and all four dealers for each base seed. The
engine assigns a seeded deck to fixed seats: changing `dealer` alone changes auction order,
**not** who holds each hand. Swapping the candidate's seat is therefore necessary. Keep all
rotations and candidate/reference replays of one base seed in the same statistical block.

Use a 500-base-seed quick validation suite during development, 2,000 for candidate selection,
and start final confirmation at 10,000 previously unused base seeds per natural-game lineup.
These are seed counts, not game counts: 10,000 seeds x 4 seats x 4 dealers x 2 policies is
320,000 games per paired lineup. Declare the lineup weights, sample size, and comparison before
the run. Bootstrap paired **base-seed blocks** for 95% confidence intervals; individual decisions,
seats, and repeated rotations are correlated and must not be counted as independent samples.
An inconclusive interval means no promotion; do not repeatedly extend the same test until it
turns positive. Investigate on validation data and predeclare a fresh confirmation run.

Initial trained-release gate:

- Zero illegal actions, hidden-information leaks, stale applications, or stalled rounds in the
  correctness and live reliability suites. Passing these gates establishes reliability only.
- Positive candidate mean raw payout against each of the two fixed heuristic styles, with its
  95% interval above zero. Random-only wins do not qualify.
- Positive paired improvement over the incumbent on the predeclared, equally weighted
  natural-game panel, with its 95% interval above zero. Keep the incumbent if this is inconclusive.
- On controlled normal Ruf, Wenz, and Solo suites, require the lower confidence bound of paired
  improvement to exceed -1 raw tariff unit per focal-seat game, an initial non-regression margin.
  Require coverage of declarer/partner/defender roles and inspect their breakdowns. Report rare
  Tout/Sie separately; deterministic Sie fixtures must pass, and broad strength claims require
  enough relevant samples. Do not bury a common-contract regression inside a pooled average.
- Live latency/fallback gates in section 9 and human playtests pass. Publish the **tested**
  bidder/player pair with its action-selection mode; a new companion invalidates pair results.

Report raw payout per deal including all-pass, paired improvement and uncertainty, role-specific
win rates, all-pass/interest/contract frequencies, Tout declaration/success rates, illegal/fallback
counts, and decision latency. Contract/role breakdowns of natural games are descriptive because
the bidder changes which contracts occur; use controlled suites for causal play comparisons.
The table-wide mean self-play payout is always zero and is not a learning-progress metric.

### G. Bound experiments by learning progress

First run a 10-update correctness smoke test, including save/restart and an actual evaluation.
Then budget an initial 1 million learner play decisions and 250,000 learner bidding decisions
for learning pilots, evaluating at start/end and every roughly 250,000/50,000 decisions.
Only expand toward a 10-million-decision play run after the pilot improves held-out payout.
These budgets are diagnostic checkpoints, not a prediction of sufficient training for a good bot.

Compare warm-start versus scratch and at most one PPO/model change at a time, first at equal
learner decisions and then by wall-clock cost. Repeat the promising setting with three independent
training seeds before a long investment. Record games/hour, learner decisions/second, collection
versus update/evaluation time, memory, and strength versus time. If three scheduled validation
checks show no improvement, inspect rewards, masks, observations, bidding, and partner dependence
before adding compute. Consider history models or information-set search only after that audit.

## 8. Implementation milestones

| Milestone | Deliverable | Acceptance gate |
| --- | --- | --- |
| 1. Environment — complete | Selected .NET executable, isolated Python environment, reproducible run manifest | SDK/runtime recorded; GPU check and existing regression tests pass |
| 2. Baseline and evaluator — complete | Versioned heuristic pair, two bidding styles, seeded paired match runner | Deterministic replays, seat/dealer coverage, block confidence intervals, and baseline reports saved |
| 3. Playable heuristic bot — complete | Server-owned named seats, conservative heuristic scheduler, automatic bot ready handling | Verified 1-3 bots across repeated played/all-pass rounds, stale/duplicate guards, reconnects, cancellation, cleanup, and 10 concurrent tables; browser Wenz/Ruf playthroughs |
| 4. Encoding | Reusable C# encoders, 17/32 action adapters, golden fixtures | Stable shapes; relative seats, history, own-role inference, privacy, and masks verified |
| 5. Batched inference and benchmark | Game pool, two TensorFlow endpoints, CPU/GPU and batch sweeps | Full games complete; timings/memory separated; partial queues and historical versions work |
| 6. Rollout collector and PPO | Per-seat/per-model buffers, losses, checkpoint/resume | Hand-calculated returns, all-pass, forced moves, learner-only data, and rollout draining verified; 10-update smoke test passes |
| 7. Warm start and playing pilot | Heuristic imitation, controlled-contract play, fresh PPO collection | Saved evaluation versus heuristic and scratch control; no claim of strength from training loss |
| 8. Bidding pilot and varied tables | Frozen-play bidding, historical pool, alternating phases | Natural-deal evaluation improves; interest/Tout behavior and unfamiliar partners assessed |
| 9. Trained live release | Frozen pair served through the already working bot scheduler | Section 7F strength gates, section 9 latency/reliability gates, and human sessions pass |

Keep the existing random-policy benchmark as a lower-overhead reference. Neural policies choose
different games, so use fixed recorded observations for isolated transport/inference comparisons,
and a separate end-to-end rollout benchmark for practical throughput. No fixed performance target
is assumed before measurement with the selected SDK and actual model shapes.

## 9. Live behavior and deployment

The live heuristic path is implemented with `heuristic-v1/conservative`, server-owned seats,
`BotTurnScheduler`, and automatic bot readiness. The table's fill control and result dialog
support setup and replacement between rounds. See [live operation](../training/README.md#live-heuristic-tables)
for pacing, pause/rejoin behavior, cleanup, and verification. Neural serving and its latency,
deadline, and fallback gates below remain future work; no neural release is claimed.

- Add explicit server-owned bot identity and a table control to fill empty seats with named bots.
  Keep bot membership distinct from human connection state; bots must satisfy table start/ready
  rules without fake browser connections. Permit seat replacement between rounds initially.
- Schedule at most one decision for the active bot and table revision. Read the detached snapshot
  under the existing lock, compute outside it, then apply through `TryApplyBotAction`. After an
  accepted action, broadcast and schedule the next active bot. Discard stale work after resets,
  seat changes, or concurrent actions; handle a removed seat without killing the scheduler.
- Cover bot-led bidding, consecutive bot turns, all-pass, end-of-round ready handling, human
  disconnect/rejoin, table cleanup, and cancellation. Bots automatically ready after settlement;
  the human retains the normal next-round control. A full game is not enough: test multiple rounds.
- Pin one release pair per live round. Run serving separately from the trainer so updates or
  trainer restarts cannot mutate weights or interrupt serving. Prefer CPU serving if it meets
  the latency target; otherwise reserve inference capacity and test concurrent training load.
  A separate process alone does not prevent GPU contention. Validate rules/encoder/action
  compatibility before accepting a model; keep the last working pair available for rollback.
- Initial target on the intended serving machine: p95 policy decision latency below 100 ms at
  one table and at a declared load of 10 simultaneous tables; a hard 1-second inference deadline.
  Measure queue wait plus encoding/transport/inference, excluding optional display pacing.
  These are usability targets to verify, not current performance claims.
- On timeout, unavailable model, or malformed response, use the local heuristic on a current
  legal observation, still subject to the revision guard. Log the reason and fallback rate;
  target less than 0.1% during a healthy-service soak, with zero stalled rounds. Inject outages
  separately to prove fallback works. Fallback actions never enter PPO as network actions.

Training samples the masked categorical policy at temperature 1. For release, initially evaluate
masked argmax with fixed tie-breaking, then compare temperature-1 sampling on validation seeds.
Choose and record one mode before final confirmation; evaluating stochastic play does not certify
greedy deployment. A later difficulty selector can use weaker validated checkpoints or temperatures
with measured strength. Do not implement difficulty through illegal moves or hidden information.

## 10. Repository and checkpoint boundaries

Proposed additions:

```text
src/sk.bot/            # C# heuristics, encoders, action adapters, inference client; references sk.shared
src/sk.training/       # C# game pool, match runner, trajectory collector; references sk.bot
training/             # Python models, inference server, imitation, PPO, orchestration, analysis
models/               # Local checkpoints and tested pair manifests; large binaries ignored by Git
```

Keep the existing `src/sk.shared`, `src/sk.api`, and `src/sk.benchmarks` names. Make inference
adapters reusable by the live server without depending on the training coordinator. Put live
seat/scheduling orchestration in `sk.api`, and keep `sk.bot` independent of table/hub services. Each
checkpoint includes weights, optimizer state, model/encoder/rules versions, training config,
seed/RNG metadata, and its companion policy version. Save evaluation results with the pair.
Also save source commit, dependency lock versions, demonstration/curriculum/opponent-pool versions,
seed split manifest, reward scale, completed update/decision counts, action-selection mode, and
promotion-suite definition. Distinguish resumable training checkpoints from immutable serving
releases. Save atomically at rollout boundaries and prove a stopped run can resume with restored
optimizer and RNG state; exact GPU bitwise reproducibility is not an acceptance requirement.
Document deterministic engine replay separately from potentially nondeterministic GPU training.

ONNX export, local .NET inference, gRPC/binary transport, recurrent models, distributed training,
and automated opponent leagues are deferred; the small frozen opponent pool above is part of
the initial trainer. If ONNX becomes worthwhile for live latency or simpler hosting, compare logits and
values against TensorFlow and verify masking/sampling parity before using it for PPO collection.
Measure CPU inference for live tables as well as GPU batching; deployment need not reserve a GPU.

If the first neural bot plateaus, inspect role/contract failures before scaling the network.
A history encoder or belief-based information-set search is a later experiment with a measured
latency budget. Search would need validated state reconstruction/cloning, which `HeadlessGame`
does not currently expose; sampled hidden states must be consistent with this seat's observations,
never the actual unseen deal. Do not make search or a second rules engine a prerequisite for v1.

The immediate next implementation is **milestone 4: observation encoding**. Batched inference
and learning follow with the evaluator and playable live target in place. The current baseline
is a fixed heuristic, not a trained model. Local browser checks do not constitute the longer
human strength playtests required for trained releases.
