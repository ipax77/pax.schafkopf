# Training and evaluation results

## Gameplay diagnostics and revised training — 2026-11-21

The [verification record](gameplay-20261121-verification.json) contains the policy comparisons,
teamwork diagnostics, and bounded training/export checks for the
[gameplay-first workflow](../README.md#gameplay-first-experiment-2026-11-21).
The original model exports remain available; no verification model was installed into the API.

The model input already contains all current-trick cards, ordered public history and known
partnerships. A regression fixture holds the hand and lead card fixed, changes the teammate's
card, and checks both the encoded trick and the teaching decision. A native audit reproduced
overtaking even when a known teammate was securely winning and an alternative legal card
could contribute at least as many points without taking over:

| Model | Such opportunities | Actual overtakes |
| --- | ---: | ---: |
| Previous selected update 250 | 122 | 71 |
| Resumed run's selected update 450 | 162 | 88 |

These counts are from three-neural-seat tables against one conservative heuristic, using 64
Validation seeds, all four focal seats and dealers. The situations differ as games diverge;
counts are descriptive and correlated, not independent strength evidence. Taking the lead
can sometimes justify an overtake, so examples are review candidates. The audit establishes
a persistent behavior worth targeting, not an overall strength regression between versions.
Full private-observation examples remain beside the local exports in `gameplay-audit-20261121.json`.

Before choosing a teacher, the changes were separated into bidding-only and playing-only
policies. Each comparison used the same 256 Validation seeds, master 20261121, start 7000000,
all seats/dealers, and 8,192 paired games against conservative opponents:

| Change from conservative policy | Paired payout change [95% seed-block interval] |
| --- | --- |
| Notes-based bidding and card play | -2.710 [-3.860, -1.626] |
| Notes-based bidding only | -2.959 [-4.073, -1.841] |
| Notes-based card play only | +0.244 [-0.198, +0.742] |

The bidder prototype is therefore optional. The recommended preset keeps **exact conservative
bidding**, uses the card-play teacher and repeats rare partner-feeding demonstrations four
times during imitation training only. The card-play comparison is inconclusive; these are
validation findings that informed the implementation, not fresh confirmation.

Verification passed 120 C# and 20 Python tests. A 2,000-game, three-epoch weighted imitation
run added 927 training rows for teamwork examples while leaving validation unweighted. The
final fixed-conservative preset completed two PPO updates / 8,816 learner decisions and all
13 evaluation panels (6,048 paired games per evaluation). Its small validation changes were
inconclusive and failed contract non-regression checks, so update 0 remained selected. A
separate fixed-gameplay-bidder pipeline test completed ten updates and resumed through update
11, restoring Adam steps, RNG and deal cursor. CPU export matched TensorFlow on 512 real
observations with zero masked-action disagreements; fixed bidder dispatch is covered by
live-policy tests for both supported rule versions.

These bounded runs verify the revised workflow, not a stronger trained bot. A substantial
imitation/PPO experiment, fresh confirmation indices, and another human playtest remain needed.

## Timed trained-bot run

The selected pair is **PPO update 250**, with the larger imitation bidder frozen. The local
export is `models/serious-20261120/` (ignored by Git); start it from the repository root in WSL:

```bash
bash training/play-trained.sh models/serious-20261120
```

Open http://localhost:5027, create a table, and choose **Freie Plätze mit Bots füllen**.
The API was restarted with this pair after user approval and confirmed `trained: true`,
the expected pair ID, and zero fallbacks. This is local experimental play, not public promotion.

The [record](serious-20261120.json) contains settings, model hashes, compact evaluation
reports, export parity, inference timings, and provenance. The
[validation curve](serious-20261120-validation.jsonl) records every selection checkpoint.
Full reports with seed blocks, Keras checkpoints, and training traces remain under
`~/.local/share/pax-schafkopf/` in the paths recorded there.

Training used the RTX 3080 Ti with a 2 GiB TensorFlow allocator cap:

- A 22.7-minute staged pilot stopped at a controlled-deal retry limit. Its learned bidder
  also collapsed toward passing (54 interests in 8,168 learner auctions). It was not selected.
  The retry limit was raised from 1,000 to 10,000 with clearer failure logs.
- A larger imitation run used 30,000 training games, 3,000 validation games, and 20 epochs
  in 6.5 minutes. The selected bidding/playing epochs were 20/11, with non-forced validation
  action accuracy 97.1%/91.6%. On 12,000 interest decisions, the bidder expressed interest in
  22.0% of hands versus the teacher's 21.2%, with 94.5% interest recall.
- The final playing PPO session completed **251 updates, 8,688,064 learner decisions,
  and 547,688 games** in 60.3 monotonic minutes (about 66 wall-clock minutes). Bidding weights
  stayed identical and its optimizer took zero steps. There were 109,420 controlled games
  and 3,640,532 rejected candidate deals; the next training index is 24,188,220.

The three sessions totaled **89.6 recorded training minutes**, excluding setup and held-out
evaluation. No checkpoint was selected using confirmation results. Update 250 had the highest
fixed-panel validation payout (+0.271 units/deal), versus -0.929 for its imitation warm start.

The predeclared [confirmation protocol](serious-20261120-protocol.json) used fresh indices
starting at 2,000,000 in the Confirmation split, all four seats/dealers, and 2,000 bootstrap
resamples of base-seed blocks. Natural comparisons each used 2,000 seed blocks and 64,000
candidate/reference games. Values below are raw payout units per deal:

| Opponents | Trained payout [95% CI] | Gain over larger imitation [95% CI] |
| --- | --- | --- |
| Three conservative heuristics | -0.468 [-0.746, -0.192] | +0.877 [+0.632, +1.131] |
| Three balanced heuristics | +0.733 [+0.342, +1.146] | +1.083 [+0.808, +1.371] |

Controlled play used 500 proposed seeds per contract, with the same forced legal auction
for both players. Ruf retained 305 eligible seeds; Wenz and Solo retained all 500:

| Contract | Gain over larger imitation [95% CI] |
| --- | --- |
| Ruf Schellen | +0.414 [-0.035, +0.846] |
| Wenz | -0.630 [-1.258, -0.054] |
| Solo Schellen | +0.706 [+0.170, +1.271] |

The pair improves over its imitation warm start, but remains below the conservative heuristic
on natural deals and regresses in controlled Wenz. Forced contracts include weak declarer
hands and measure card play separately from bidding. These results do not establish expert
strength or satisfy the public-release gate; the models remain experimental.

All **169,760 confirmation games** completed without illegal actions. C# CPU inference
matched TensorFlow on 256 real bidding and 256 real playing observations: zero masked-action
disagreements, maximum logit error 0.00000771, and maximum value error 0.000000985. Measured
decision p95 was 0.578 ms with one worker and 0.125 ms with ten workers (encoding and inference
only, excluding scheduler queues/network/display pacing; not a live-table soak). Regression
verification passed **108 C# tests and 16 Python tests**. Existing NuGet audit warnings remain.

## Initial baseline

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
