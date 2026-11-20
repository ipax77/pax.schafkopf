# WSL training and live bot plan

Status: agreed design direction; implementation milestones below are not yet completed.
Updated: 2026-11-20.

This is the implementation plan following the engine redesign and performance work.
[engine.md](engine.md) defines the authoritative rules and action IDs.
[tensorflow.md](tensorflow.md) is the original architecture proposal; where it differs,
this plan takes precedence. No TensorFlow dependency belongs in `sk.shared`.

## 1. Objective and decisions

Train bots that can participate in both bidding and card play, then use the same policies
to fill seats at live tables. Preserve one C# rules implementation for training and live games.

| Area | Decision |
| --- | --- |
| Simulator | Existing .NET 10 `HeadlessGame`, running in WSL |
| Learning | Python/TensorFlow, initially synchronous PPO |
| Policies | Separate bidding and playing models, each with its own value head |
| Seats | Share each model across all four seats; encode positions relative to the acting seat |
| Transport | Batched local HTTP for the first implementation |
| Scheduling | Separate bidding and playing queues, served by one Python process |
| Rollouts | Complete games, with a fixed pair of model versions during collection |
| Reward | Acting seat's terminal payout, with one documented constant scale |
| Initial learning | Train play under controlled contracts, then bidding against frozen play |
| Subsequent learning | Alternate training phases, freezing the other model |
| Deployment | Policy adapters through existing live service hooks; ONNX is a later option |

The current single-pass, two-stage auction remains unchanged. Rule variants and a redesigned
auction are separate work and would require versioned training data.

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

- Own hand as card-presence features; phase and contract, including Tout.
- Dealer, leader, declarer, current player, and public partner relative to the observing seat.
- Hand sizes, captured points, and trick counts in the same relative seat order.
- Current trick, distinguishing absent cards from actual card ID zero.
- Public interest/declaration history and ordered played-card history, with actor positions.
- Ran-away state and explicit unknown/absent indicators.

Use fixed padded slots with validity masks for the bounded public history: up to eight bidding
actions and 32 card plays. Start with feed-forward models; recurrent models are a later experiment.
Use categorical/card encodings rather than treating raw card IDs as ordinal scalar values.
Document which features each model uses and freeze ordering in golden encoding fixtures.

Never supply opponent hands, terminal starting-hand reveals, or an unrevealed partner assignment.
An ace holder may infer partnership from their own hand. Use `Observe`, not the UI results DTO.
Legality comes from the engine mask, which must also be recorded with the observation.

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

Reject all-false masks rather than sampling arbitrary actions. Apply the identical mask when
computing PPO losses and entropy. Do not silently replace an invalid model action with a random
one and then train on its original log probability. Retries must not apply a transition twice.

No per-card HTTP requests and no SignalR in the training loop. Live inference may use small
batches and a short wait limit; it must not wait for a training-sized batch.

## 6. Trajectories, rewards, and PPO updates

Record episode ID, seat, model kind, observation, legal mask, action, old masked log probability,
value estimate, reward, terminal flag, decision index, and **both** model versions. Record rules,
encoder, and reward-scale versions in the dataset/run metadata.

Group transitions by `(episode, seat, model kind)`. Successive actions by different players
are not adjacent steps in one agent's PPO sequence. For each model's sequence, zero intermediate
reward leads to that seat's terminal payout; a bidding sequence ends only when the game ends.
Both models optimize the same outcome through their respective decisions. Do not concatenate
their independent reward streams and accidentally count terminal payout twice in one return.

All four players receive their payout on termination, even though only one made the last move.
All-pass games produce valid zero-reward bidding trajectories and no playing trajectories.
This matters when a player has passed or made their final card decision before the game finishes.

Initially use complete episodes and gamma 1.0. GAE lambda 0.95, four PPO epochs, and minibatch
size 2,048 are provisional starting points, to be tested separately for each model. Size rollout
targets in each model's own decisions; bidding supplies far fewer samples than play.
Finish active games before updating a model, even if a nominal decision-count target is exceeded.
This avoids truncated-episode bootstrapping in the initial implementation.

Freeze the policy pair throughout a collection period. Train only the currently selected model;
then publish a new pair manifest at a rollout boundary. If historical opponents are introduced,
tag behavior versions and train only on transitions generated by the learner policy for that update.

## 7. Training stages and evaluation

### A. Establish card play

Use a controlled distribution of legal Ruf, Wenz, Solo, and Tout contracts, rotating declarer
and dealer seats. Construct contracts through validated auction actions; do not bypass engine
state transitions or use hidden information in the policy input. Dedicated fixtures cover rare Sie.
Record the curriculum distribution and evaluate contracts separately. A scripted bidder provides
contracts during this stage; its decisions are not PPO training data for the bidding model.

### B. Learn bidding

Freeze a selected playing checkpoint. Train the bidding model on natural seeded deals and
full-game payouts. Evaluate against fixed bidding baselines using the same frozen playing model
so improvements can be attributed to bidding.

### C. Alternate improvements

Freeze bidding while improving play, then freeze play while improving bidding. Retain some
controlled-contract play training to prevent a narrow bidding policy from starving other contracts.
Periodically evaluate the combined pair and keep previous pairs as reference opponents.

Evaluation uses held-out seeds, rotated seats, fixed random/heuristic opponents, and frozen
historical model pairs. Report mean payout with uncertainty, win rates, contract frequencies,
all-pass frequency, Tout success, illegal-action count, and per-contract performance.
The table-wide mean self-play payout is always zero and is not a learning-progress metric.
Do not claim a strong bot based solely on training loss or beating a random player.

## 8. Implementation milestones

| Milestone | Deliverable | Acceptance gate |
| --- | --- | --- |
| 1. Environment | Selected .NET executable, isolated Python environment, reproducible run manifest | SDK/runtime recorded; GPU check and existing regression tests pass |
| 2. Encoding | C# encoders, 17/32 action adapters, golden fixtures | Stable shapes; correct relative seats; privacy and mask tests pass |
| 3. Batched inference | Game pool and two TensorFlow endpoints using untrained models | Complete games without invalid actions, stale applications, or queue deadlocks |
| 4. Inference benchmark | Batch sweep at 64, 256, 1,024, and 4,096; CPU/GPU comparisons | Separate encoding, JSON/transport, inference, simulation, and end-to-end measurements |
| 5. Rollout collector | Per-seat/per-model buffers and terminal reward routing | Hand-calculated returns, all-pass cases, masks, and version boundaries verified |
| 6. Playing PPO | Controlled-contract training and checkpoint/resume | Short training run completes; optimizer state resumes; held-out baseline evaluation saved |
| 7. Bidding PPO | Frozen-play training followed by alternating phases | Both models receive correct samples; pair-based evaluation and checkpoints work |
| 8. Live integration | Seat/policy scheduling using existing service hooks | Mixed human/bot tables progress; stale actions are rejected; timeout fallback is legal |

Keep the existing random-policy benchmark as a lower-overhead reference. Neural policies choose
different games, so use fixed recorded observations for isolated transport/inference comparisons,
and a separate end-to-end rollout benchmark for practical throughput. No fixed performance target
is assumed before measurement with the selected SDK and actual model shapes.

## 9. Repository and deployment boundaries

Proposed additions:

```text
src/sk.training/       # C# game pool, encoders, action adapters, policy client, trajectory collector
training/             # Python models, inference server, PPO, orchestration, evaluation
models/               # Local checkpoints and tested pair manifests; large binaries ignored by Git
```

Keep the existing `src/sk.shared`, `src/sk.api`, and `src/sk.benchmarks` names. Make inference
adapters reusable by the live server without depending on the training coordinator. Each
checkpoint includes weights, optimizer state, model/encoder/rules versions, training config,
seed/RNG metadata, and its companion policy version. Save evaluation results with the pair.
Document deterministic engine replay separately from potentially nondeterministic GPU training.

ONNX export, local .NET inference, gRPC/binary transport, recurrent models, distributed training,
and automated opponent leagues are deferred. If ONNX becomes worthwhile, compare logits and
values against TensorFlow and verify masking/sampling parity before using it for PPO collection.
Measure CPU inference for live tables as well as GPU batching; deployment need not reserve a GPU.

The immediate next task is **milestones 1–2**, followed by the two-endpoint inference benchmark.
