# Headless simulation audit

Run from the repository root with .NET 10:

```sh
dotnet run --project tools/sk.simulation-audit -c Release -- 1000000
```

The runner references only `sk.shared` and uses the same validated engine as the live game.
It fails immediately on empty legal actions, nontermination, invalid terminal rewards,
missing/duplicate played cards, incorrect hand sizes, or failure to conserve tricks/points.
Targeted rule and scoring regressions live in `src/sk.tests`; run those as well.

## Workload

One sequential worker, 2,000 warm-up episodes, then the requested count.
Deal seed equals episode index; dealer rotates. Equal numbers of all-pass, one-bidder,
two-bidder, and four-bidder deals exercise both bidding phases.
The seeded action RNG samples all legal declarations (including Tout) and legal cards.
Each decision after phase one receives a detached seat-limited policy observation.
Every step validates its action. Episode reset bounds history automatically.

The benchmark includes observation creation, scoring, and invariant checks, but excludes
networking, serialization, tensor encoding, neural inference, and gradient updates.
This is simulation throughput, not training throughput. Sie is rare and has a dedicated test fixture.

## WSL result (2026-11-20)

Ubuntu 24.04.5 / .NET 10.0.15, 24 logical CPUs available, one worker; `schafkopf-long-v1`:

| Metric | Result |
| --- | ---: |
| Episodes | 1,000,000 |
| Complete 32-card games | 750,000 |
| All-pass deals | 250,000 |
| Stalled games | 0 |
| Tout contracts | 480,791 |
| Elapsed simulation time | 33.900 s |
| Episodes/s | 29,499 |
| Completed games/s (mixed workload) | 22,124 |
| Actions/s | 877,587 |
| Allocated bytes/episode | 90,472 |
| Gen0 collections | 5,768 |

Contracts: Solo 601,363; Wenz 100,603; Ruf 48,033; Sie 1.
Allocation is cumulative, not retained memory. Timing is an initial measurement, not a guarantee.

The pre-redesign audit took 28–32 seconds per million episodes and stalled roughly 2,900
Ruf games per run; it allocated about 114 KB/episode. That workload excluded Tout, used
unseeded deals and mutable public DTOs, and did not validate submitted actions. The new run
has different contract frequencies and additional validation, so these timings are not a
controlled before/after speed comparison.

See [the engine contract](../../docs/engine.md) for rules, action IDs, replay, and live bot hooks.
