using sk.shared;

namespace sk.bot;

/// <summary>Versioned teaching policy from docs/gameplay.md. Guidance, not additional game rules.</summary>
public sealed class GameplayPolicy : IGamePolicy
{
    public const string Version = "gameplay-v1";
    public string Id => Version;
    private readonly HeuristicPolicy fallback = new(BiddingStyle.Conservative);

    public bool AcceptsContract(GameObservation observation, GameAction bid) =>
        bid.Kind == ActionKind.Bid && BidScore(observation, bid) >= 0;

    private double BidScore(GameObservation observation, GameAction bid)
    {
        var hand = observation.Hand.Select(GameAction.FromCardId).ToArray();
        if (bid.GameType == GameType.Sie) return 100;
        if (bid.Tout) return fallback.AcceptsContract(observation, bid) ? 50 : -1000;
        var trump = hand.Where(c => c.IsTrump(bid.GameType, bid.Suit)).ToArray();
        var side = hand.Except(trump).ToArray();
        int position = (observation.Seat - observation.Dealer + 3) % 4; // 0 = opening lead, 3 = rear hand.
        int aces = side.Count(c => c.Rank == Rank.Ace);
        switch (bid.GameType)
        {
            case GameType.Ruf:
                return trump.Length >= 5 && side.Where(c => c.Suit != bid.Suit).Select(c => c.Suit).Distinct().Count() <= 1
                    ? trump.Length - 5 + .25 * aces : -1000;
            case GameType.Wenz:
                bool flute = side.GroupBy(c => c.Suit).Any(g => g.Count() >= 4 && g.Any(c => c.Rank == Rank.Ace));
                return (aces >= 3 && (position == 0 || trump.Length >= 2)) || (trump.Length >= 2 && flute)
                    ? 2 + .5 * trump.Length + .25 * aces + (position == 0 ? .5 : 0) : -1000;
            case GameType.Solo:
                // A deliberately conservative proxy for 2–3 losing tricks, not a double-dummy claim:
                // six trumps, one side suit without a ten, and at least one of the top two trumps.
                var top = Enumerable.Range(0, 32).Select(GameAction.FromCardId)
                    .Where(c => c.IsTrump(bid.GameType, bid.Suit))
                    .OrderByDescending(c => c.GetCardRank(bid.GameType, bid.Suit)).Take(2);
                return trump.Length >= 6 && side.Select(c => c.Suit).Distinct().Count() <= 1 &&
                    !side.Any(c => c.Rank == Rank.Ten) && top.Any(trump.Contains)
                    ? 3 + trump.Length - 6 + .25 * aces + (position == 3 ? .5 : 0) : -1000;
            default: return -1000;
        }
    }

    public GameAction ChooseAction(GameObservation observation)
    {
        if (observation.ActivePlayer != observation.Seat || observation.LegalActions.Count == 0)
            throw new InvalidOperationException("A policy needs an active, nonterminal observation.");
        if (observation.LegalActions.Count == 1) return observation.LegalActions[0];
        if (observation.Phase == GameState.Playing)
        {
            var advice = PlayAdvice(observation);
            return fallback.ChooseAction(advice == null ? observation : observation with { LegalActions = advice.Actions });
        }
        var bids = observation.Phase == GameState.Bidding1
            ? GameRules.LegalBids(observation.Hand.Select(GameAction.FromCardId).ToArray(), false)
            : observation.LegalActions.Where(a => a.Kind == ActionKind.Bid);
        if (observation.Contract is { } current)
            bids = bids.Where(a => GameRules.BidRank(a.GameType, a.Tout) > GameRules.BidRank(current.GameType, current.Tout));
        var best = bids.Select(a => (Action: a, Score: BidScore(observation, a)))
            .OrderByDescending(x => x.Score).ThenBy(x => x.Action.Id)
            .FirstOrDefault((GameAction.Pass, -1000));
        if (observation.Phase == GameState.Bidding1) return GameAction.Interest(best.Score >= 0);
        if (best.Score >= 0) return best.Action;
        return observation.LegalActions.Contains(GameAction.Pass) ? GameAction.Pass : fallback.ChooseAction(observation);
    }

    public sealed record Advice(string Reason, GameAction[] Actions, GameAction[]? OvertakingActions = null);

    public static Advice? PlayAdvice(GameObservation observation)
    {
        if (observation.Phase != GameState.Playing || observation.Contract is not { } contract ||
            observation.LegalActions.Count < 2) return null;
        int played = observation.CurrentTrick.Count(id => id >= 0);
        if (played == 0) return LeadAdvice(observation);
        var lead = GameAction.FromCardId(observation.CurrentTrick[observation.LeadingPlayer]);
        int RankOf(Card c) => c.GetCardRank(contract.GameType, contract.Suit, lead);
        int winner = Enumerable.Range(0, 4).Where(s => observation.CurrentTrick[s] >= 0)
            .OrderByDescending(s => RankOf(GameAction.FromCardId(observation.CurrentTrick[s]))).First();
        if (!HeuristicPolicy.AreTeammates(observation, winner)) return null;
        int winningRank = RankOf(GameAction.FromCardId(observation.CurrentTrick[winner]));
        var known = observation.Hand.Concat(observation.CurrentTrick.Where(id => id >= 0))
            .Concat(observation.History.Where(h => h.Action.Kind == ActionKind.Play).Select(h => h.Action.CardId)).ToHashSet();
        bool secure = played == 3 || !Enumerable.Range(0, 32).Any(id => !known.Contains(id) && RankOf(GameAction.FromCardId(id)) > winningRank);
        if (!secure) return null;
        var losing = observation.LegalActions.Where(a => RankOf(GameAction.FromCardId(a.CardId)) < winningRank).ToArray();
        var taking = observation.LegalActions.Except(losing).ToArray();
        if (losing.Length == 0 || taking.Length == 0) return null;
        // A narrow, auditable case: preserve the partner's secure winner when we can
        // contribute at least as many points without taking over. Other overtakes may be sound.
        int takingPoints = taking.Max(a => GameAction.FromCardId(a.CardId).GetValue());
        var feeding = losing.Where(a => GameAction.FromCardId(a.CardId).GetValue() >= takingPoints).ToArray();
        return feeding.Length > 0 ? new("preserve-secure-partner-winner", feeding, taking) : null;
    }

    /// <summary>Leading preferences from private cards and public history only. Never constrain the learner's legal mask.</summary>
    public static Advice? LeadAdvice(GameObservation observation)
    {
        if (observation.Phase != GameState.Playing || observation.Contract is not { } contract ||
            observation.CurrentTrick.Any(id => id >= 0) || observation.LegalActions.Count < 2) return null;
        bool Trump(Card c) => c.IsTrump(contract.GameType, contract.Suit);
        var legal = observation.LegalActions;
        var known = observation.Hand.Concat(observation.History.Where(h => h.Action.Kind == ActionKind.Play)
            .Select(h => h.Action.CardId)).ToHashSet();
        bool outstandingTrump = Enumerable.Range(0, 32).Any(id => !known.Contains(id) && Trump(GameAction.FromCardId(id)));
        bool declaringSide = observation.Seat == contract.Player || HeuristicPolicy.AreTeammates(observation, contract.Player);
        var trumps = legal.Where(a => Trump(GameAction.FromCardId(a.CardId))).ToArray();
        if (declaringSide && outstandingTrump && trumps.Length > 0 && trumps.Length < legal.Count)
            return new("declaring-side-draw-trumps", trumps);
        var sides = legal.Where(a => !Trump(GameAction.FromCardId(a.CardId))).ToArray();
        if (sides.Length == 0) return null;
        if (!declaringSide && contract.GameType == GameType.Ruf && observation.PublicTeammate < 0 && !observation.RanAway)
        {
            var search = sides.Where(a => GameAction.FromCardId(a.CardId).Suit == contract.Suit).ToArray();
            if (search.Length > 0 && search.Length < legal.Count) return new("defender-search-called-ace", search);
        }
        // Every fourth public play is a trick lead. Prefer a suit not yet led; after all
        // suits have been tried, allow repeats. Once trumps are exhausted, cashing winners wins.
        var led = observation.History.Where(h => h.Action.Kind == ActionKind.Play).Where((_, i) => i % 4 == 0)
            .Select(h => GameAction.FromCardId(h.Action.CardId)).Where(c => !Trump(c)).Select(c => c.Suit).ToHashSet();
        var fresh = sides.Where(a => !led.Contains(GameAction.FromCardId(a.CardId).Suit)).ToArray();
        if (!declaringSide && outstandingTrump && fresh.Length > 0 && fresh.Length < legal.Count)
            return new("defender-lead-fresh-suit", fresh);
        if (!declaringSide && sides.Length < legal.Count) return new("defender-lead-side-suit", sides);
        return null;
    }
}

/// <summary>Use exactly the same fixed bidder for collection, evaluation and exported play.</summary>
public sealed class FixedBiddingPolicy(IGamePolicy bidder, IGamePolicy player) : IGamePolicy
{
    public GameAction ChooseAction(GameObservation observation) =>
        (observation.Phase == GameState.Playing ? player : bidder).ChooseAction(observation);
}
