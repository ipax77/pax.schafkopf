# Deterministic Schafkopf engine

The package-free `sk.shared` project owns the rules used by the live server, UI hints,
and the headless environment. `HeadlessGame.RulesVersion` is `schafkopf-long-v1`.
Record this version with training data, plus the seed, dealer seat, and ordered action IDs.

## Supported rules

- Four players, 32 cards, eight tricks; seats and dealer are numbered 0..3.
- Ruf (Schellen/Gras/Eichel), Wenz, four suit Solos, Wenz-Tout, Solo-Tout, and Sie.
  Ramsch, short deck, Farbwenz, Geier, Contra/Re, and doubling are not implemented.
  Unsupported declarations are rejected. All-pass ends an episode with zero reward.
- The app's existing two-stage **single-pass** auction is retained: four interest decisions,
  then one declaration/pass per interested seat, starting left of dealer. This is not an
  iterative raise/rebid auction. Legal Ruf declarations are allowed with multiple interested players.
  The last interested player must declare if nobody has declared a contract.
  Priority: Sie > Solo-Tout > Wenz-Tout > Solo > Wenz > Ruf. Equal bids go to the earlier seat
  in auction order. Lower declarations are allowed and do not change the winning bid.
- Ruf requires a non-trump card of the called suit and no ace of that suit in the caller's hand.
  Follow trump/suit when possible. The called ace must be played when its suit is searched,
  and cannot otherwise be discarded before the last trick unless its holder ran away.
  Leading below it requires at least four currently held non-trump cards in the called suit.
  Running away reveals the partner and releases the ace restriction for subsequent play.
- Normal declarers need 61 points; defenders win at 60. Declarers are Schneider at <=30,
  defenders at <30. Schwarz and Tout use tricks, not points. Losing a zero-point trick loses Tout.
- Tariff units preserve the app's 10 Ruf / 20 solo base, plus 10 per qualifying runner,
  10 Schneider, and 10 Schwarz. Runners count from the highest trump on either side,
  through all trump ranks (minimum three for Ruf/Solo, two for Wenz).
  Tout doubles base plus runners, without Schneider/Schwarz surcharges.
  Sie requires all four Ober and Unter and quadruples base plus runners. Simulation plays
  its eight forced winning tricks; a separate Tout flag does not change Sie's tariff.

These are explicit choices for this ruleset, not configurable house rules. Rule/tariff changes
must update the version and regression fixtures before reusing training data.
References: [Sauspiel rules](https://www.sauspiel.de/hilfe/schafkopf-regeln),
[settlement](https://www.sauspiel.de/schafkopf-lernen/spielabrechnung).

## Headless usage

```csharp
var game = new HeadlessGame();
game.Reset(seed: 42, dealer: 3); // required before the first action
while (!game.IsTerminal)
{
    var observation = game.Observe(game.CurrentPlayer);
    bool[] mask = observation.LegalActionMask();
    // Replace this baseline with an IGamePolicy or a masked model prediction.
    int actionId = observation.LegalActions[0].Id;
    StepResult step = game.Step(actionId);
    // Rewards are four seat-ordered payout deltas, zero until terminal.
    // Consume the terminal reward once and Reset for the next episode.
}
```

`Reset(seed, dealer)` discards all episode history and rewards. Dealing uses specified SplitMix64
output, rejection sampling, and Fisher-Yates rather than runtime-dependent shuffle behavior.
The tests pin the full deck for seed 42. Replay uses the same seed, dealer, and action IDs.
To rotate seats over an evaluation suite, supply a different dealer explicitly.

`Observe(seat)` returns read-only copied collections and value-only records: own cards,
all hand sizes, contract, current trick, per-seat points/trick counts, public partner information,
and ordered public bidding/play history. Only the active seat has legal actions.
No opponent hand, starting hand, unrevealed team assignment, identity, or connection ID is exposed,
including after termination. An ace holder can infer their own partnership from their own hand.
Observations and rewards remain unchanged after later steps/resets.

| Action IDs | Meaning |
| --- | --- |
| 0, 1 | Decline / express interest |
| 2 | Pass during declaration |
| 3, 4, 5 | Ruf Schellen / Gras / Eichel |
| 6, 7 | Wenz / Wenz-Tout |
| 8..15 | Solo / Solo-Tout pairs, in Schellen, Herz, Gras, Eichel order |
| 16 | Sie |
| 17..48 | Play card ID 0..31 |

Card IDs use `(suit - 1) * 8 + (rank - 1)`, with the existing enum order:
Schellen, Herz, Gras, Eichel; Seven, Eight, Nine, Unter, Ober, King, Ten, Ace.
`GameAction.FromId` and `FromCardId` decode IDs; `LegalActionMask()` always has 49 entries.
Illegal actions, wrong turns, and post-terminal actions throw without changing an engine-owned
episode. One instance has one owner; parallel workers must use separate instances.
The [versioned C# observation encoder](observation-encoding.md) feeds the sample TensorFlow
imitation trainer and batched stdio evaluator. PPO rollouts and HTTP serving are later layers.

## Live integration

The existing `Game` methods and public-state helpers use `GameRules`, the same implementation as
`HeadlessGame`. `Game` remains the legacy mutable table/session model for application compatibility;
do not hand it to a policy or mutate its fields to drive a game. Use validated action methods.
Live `Reset()` retains cash and the session results ledger and rotates dealer; headless reset does not.

The service serializes mutations and snapshot delivery per table. `BotTurnScheduler` uses:

1. Read `GameHubService.GetActiveBotTurn(gameId)` for the active bot's detached observation and revision.
2. Compute the policy decision outside the table lock (e.g. through `IGamePolicy`).
3. Call `TryApplyBotAction(gameId, playerId, revision, action)`; discard/recompute if it returns false.
4. Broadcast the updated table after an accepted action.

The revision guards delayed/concurrent decisions, including round resets, lineup changes, and
disconnect/rejoin. Only server-owned bot seats can apply bot actions; human commands and bot commands
use the same engine authority. Live seats use `heuristic-v1/conservative`, automatically ready at
settlement, and can be replaced between rounds. Model hosting remains future work. See the
[live bot guide](../training/README.md#live-heuristic-tables) for controls, pacing, and cleanup.
The UI DTO still reveals starting hands after a round for the results dialog; policy observations
deliberately do not use that DTO.

## Verification

```sh
dotnet test src/sk.tests/sk.tests.csproj -c Release
dotnet build src/sk.pwa/sk.pwa.csproj -c Release
dotnet run --project tools/sk.simulation-audit -c Release -- 1000000
```

Tests cover invalid-command atomicity, called-ace rules, scoring thresholds, lost zero-point
Tout tricks, defensive/long runners, Sie, observation privacy, seeded deals, terminal rewards,
action IDs, concurrent/stale live bot decisions, and 500 seeded games. Randomized tests compare
public UI masks, direct live actions, and the headless environment at every step and check
card/point conservation. These complement targeted fixtures rather than independently
implementing the entire ruleset.
