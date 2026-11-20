# Engine benchmarks

Run in Release, preferably in WSL on the training machine, from the repository root:

```sh
dotnet run --project src/sk.benchmarks -c Release -- /tmp/current.json
# Check behavioral checksums and report ratios against a saved run:
dotnet run --project src/sk.benchmarks -c Release -- /tmp/current.json src/sk.benchmarks/results/baseline.json
```

This is a small dependency-free repeated-batch harness, not BenchmarkDotNet. It is intended
for coarse optimization passes and end-to-end comparisons, not rigorous nanosecond estimates.
It warms each case for at least one second, measures seven fixed-size batches with
`Stopwatch.GetTimestamp`, and records median/min/max time, all samples, and current-thread
allocated bytes. Collections occur before timing; any GC during the batch is included.
There is no timing assertion in CI. Microbenchmark numbers include delegate/checksum overhead;
use the full-game cases as the primary performance evidence.

Run benchmarks alone, without tests/builds competing for CPU. Compare the same hardware,
runtime, build mode, and workload. For Windows and WSL sharing a checkout, isolate build outputs
with `--artifacts-path /tmp/schafkopf-bench`. Results exclude neural inference, IPC, serialization,
and training. All cases use one simulation worker; logical CPU count is machine metadata.

## Cases

- **Deal:** shuffle and deal 32 cards, including hand/starting-hand allocation.
- **LegalCards:** repeated legal-card generation for one fixed follow-suit position.
- **Observe:** detached policy snapshot for the same early-trick position, including legal actions.
- **Auction:** seeded reset, four interest declarations, then all four players' bids/passes.
- **FullGame:** reset, bidding, all 32 plays, settlement and a final observation. Uses legal-action
  lists for decisions. Every episode has at least one interested player, so none is an all-pass deal.
- **FullGameWithObservations:** identical games, with a detached observation at every decision.
  This is the closest case to a future policy-driven worker.

Full-game samples each replay 10,000 fixed deals with fixed action selection, covering Ruf,
Wenz, Solo, and Tout. Checksums fold legal action IDs in order, chosen actions, rewards,
and final points. Both full-game cases must agree; comparing to a saved baseline throws on
changed checksums or operation counts. Checksums supplement, rather than replace, regression
tests for privacy, rejection of invalid actions, rare Sie hands, and rules correctness.

## Two optimization rounds — 2026-11-20

Measured under Ubuntu 24.04.5 WSL2, .NET 10.0.15, 24 logical CPUs, single worker.
Baseline engine: commit `5331cab` (`fix game engine`). Saved JSON reports contain all samples.
The first-round report was repeated without concurrent test activity before saving these numbers.

| Case | Baseline µs/op | Round 1 µs/op | Round 2 µs/op | Baseline B/op | Round 2 B/op |
| --- | ---: | ---: | ---: | ---: | ---: |
| Deal | 0.652 | 0.380 | 0.397 | 2,480 | 960 |
| LegalCards | 0.158 | 0.028 | 0.027 | 256 | 120 |
| Observe | 0.436 | 0.301 | 0.139 | 1,584 | 968 |
| Auction | 6.375 | 4.189 | 1.813 | 25,906 | 5,376 |
| FullGame | 31.287 | 15.151 | 11.447 | 67,740 | 22,755 |
| FullGameWithObservations | 50.497 | 33.997 | 19.465 | 124,061 | 62,019 |

**Full games with policy observations: 2.59x throughput, 50.0% less allocation.**
Full games without per-decision observations: 2.73x throughput, 66.4% less allocation.
These are local warm-process measurements, not training-speed guarantees or universal targets.

Round 1:

- Remove duplicate complete legal-action generation in `Step`. Public action handlers still
  validate phase/legality; `Step` checks canonical command shape so extraneous fields remain invalid.
- Share the immutable 32-card objects and nonterminal zero-reward result.
- Use stack-allocated shuffle indices and a value-type PRNG without changing the shuffle sequence.
- Replace temporary LINQ/filter collections in legal-card generation with bounded scans.

Round 2:

- Construct detached observation arrays directly; avoid copying an already-private legal-action list.
- Replace LINQ/hash-set work in bid generation and avoid sorting to find the highest bid.
- Resolve tricks directly over the four cards without tuple lists or comparer allocations.
- Precompute private immutable trump order tables for runner scoring.

All six case checksums match the baseline after both rounds; both full-game checksums are
`49792B2969824678`. The rules/action version remains `schafkopf-long-v1`.
All 67 regression tests pass under WSL, including a new check for malformed action records and
immutability of shared cards/rewards. Observation collections remain detached and read-only.

The separate million-episode simulation audit completed in 14.864 seconds after round 2
(750,000 complete games, 250,000 all-pass deals, zero stalls, 44,510 allocated bytes/episode).
Contract counts matched the previous audit, including its rare Sie. That audit uses a different
workload from the full-game benchmarks above and should not be mixed into their ratios.
