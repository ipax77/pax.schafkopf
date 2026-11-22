using sk.shared;

namespace sk.bot;

public enum BiddingStyle { Balanced, Conservative }

/// <summary>Stateless baseline. Only the acting seat's observation reaches this policy.</summary>
public sealed class HeuristicPolicy(BiddingStyle style = BiddingStyle.Balanced) : IGamePolicy
{
    public const string Version = "heuristic-v1";
    public string Id => $"{Version}/{style.ToString().ToLowerInvariant()}";

    // Curriculum acceptance depends only on the prospective declarer's own hand.
    public bool AcceptsContract(GameObservation observation, GameAction bid) =>
        bid.Kind == ActionKind.Bid && BidScore(observation.Hand.Select(GameAction.FromCardId).ToArray(), bid) >= 0;

    public GameAction ChooseAction(GameObservation observation)
    {
        if (observation.ActivePlayer != observation.Seat || observation.LegalActions.Count == 0)
            throw new InvalidOperationException("A policy needs an active, nonterminal observation.");
        if (observation.LegalActions.Count == 1) return observation.LegalActions[0];
        return observation.Phase switch
        {
            GameState.Bidding1 => GameAction.Interest(BestBid(observation,
                GameRules.LegalBids(observation.Hand.Select(GameAction.FromCardId).ToArray(), false)).Score >= 0),
            GameState.Bidding2 => ChooseBid(observation),
            GameState.Playing => ChooseCard(observation),
            _ => throw new InvalidOperationException("Unsupported policy phase.")
        };
    }

    private GameAction ChooseBid(GameObservation observation)
    {
        bool canPass = observation.LegalActions.Contains(GameAction.Pass);
        var bids = observation.LegalActions.Where(a => a.Kind == ActionKind.Bid);
        if (canPass && observation.Contract is { GameType: not GameType.None } current)
            bids = bids.Where(a => GameRules.BidRank(a.GameType, a.Tout) >
                GameRules.BidRank(current.GameType, current.Tout));
        var best = BestBid(observation, bids);
        return canPass && best.Score < 0 ? GameAction.Pass : best.Action;
    }

    private (GameAction Action, double Score) BestBid(GameObservation observation, IEnumerable<GameAction> bids)
    {
        var hand = observation.Hand.Select(GameAction.FromCardId).ToArray();
        return bids.Select(a => (Action: a, Score: BidScore(hand, a)))
            .OrderByDescending(x => x.Score).ThenBy(x => x.Action.Id)
            .FirstOrDefault((GameAction.Pass, double.NegativeInfinity));
    }

    private double BidScore(Card[] hand, GameAction bid)
    {
        if (bid.GameType == GameType.Sie) return 100;
        // Sufficient, deliberately narrow Tout condition: own every Wenz trump and
        // a contiguous sequence from the ace in every held side suit. No unseen cards needed.
        if (bid.Tout) return GuaranteedWenzTout(hand, bid) ? 50 : double.NegativeInfinity;
        var trumps = hand.Where(c => c.IsTrump(bid.GameType, bid.Suit)).ToArray();
        var side = hand.Except(trumps).ToArray();
        double aces = side.Count(c => c.Rank == Rank.Ace);
        double coveredTens = side.Count(c => c.Rank == Rank.Ten &&
            side.Any(a => a.Suit == c.Suit && a.Rank == Rank.Ace));
        double voids = Enumerable.Range(1, 4).Count(s => !side.Any(c => (int)c.Suit == s));
        double high = trumps.Count(c => c.Suit is Suit.Gras or Suit.Eichel && c.Rank is Rank.Ober or Rank.Unter);
        double score = bid.GameType switch
        {
            GameType.Ruf => trumps.Length + 0.65 * trumps.Count(c => c.Rank == Rank.Ober) +
                0.35 * high + 1.4 * aces + 0.3 * voids - 0.25 * side.Count(c => c.Suit == bid.Suit) - 6.0,
            GameType.Wenz => 2.0 * trumps.Length + 0.5 * high + 1.5 * aces +
                0.5 * coveredTens + 0.25 * voids - 8.5,
            GameType.Solo => trumps.Length + 0.65 * trumps.Count(c => c.Rank == Rank.Ober) +
                0.35 * high + 1.4 * aces + 0.3 * voids - 9.0,
            _ => double.NegativeInfinity
        };
        return score - (style == BiddingStyle.Conservative ? 1.0 : 0.0);
    }

    private static bool GuaranteedWenzTout(Card[] hand, GameAction bid)
    {
        if (bid.GameType != GameType.Wenz || hand.Count(c => c.Rank == Rank.Unter) != 4) return false;
        foreach (var group in hand.Where(c => c.Rank != Rank.Unter).GroupBy(c => c.Suit))
        {
            var highest = Enumerable.Range(0, 32).Select(GameAction.FromCardId)
                .Where(c => c.Suit == group.Key && c.Rank != Rank.Unter)
                .OrderByDescending(c => c.GetCardRank(GameType.Wenz, Suit.None)).Take(group.Count());
            if (highest.Any(c => !group.Contains(c))) return false;
        }
        return true;
    }

    private static GameAction ChooseCard(GameObservation observation)
    {
        var contract = observation.Contract ?? throw new InvalidOperationException("Play requires a contract.");
        var cards = observation.LegalActions.Select(a => (Action: a, Card: GameAction.FromCardId(a.CardId))).ToArray();
        var known = observation.Hand.Concat(observation.History.Where(h => h.Action.Kind == ActionKind.Play)
            .Select(h => h.Action.CardId)).ToHashSet();
        var unseen = Enumerable.Range(0, 32).Where(id => !known.Contains(id)).Select(GameAction.FromCardId).ToArray();
        bool Trump(Card c) => c.IsTrump(contract.GameType, contract.Suit);
        int RankOf(Card c, Card? lead = null) => c.GetCardRank(contract.GameType, contract.Suit, lead);
        double Cost(Card c) => c.GetValue() * 2 + (Trump(c) ? 8 : 0) + RankOf(c) / 100.0;
        bool Master(Card c) => !unseen.Any(u => Trump(u) == Trump(c) &&
            (Trump(c) || c.Suit == u.Suit) && RankOf(u) > RankOf(c));
        int played = observation.CurrentTrick.Count(id => id >= 0);
        if (played == 0)
        {
            bool declaringSide = observation.Seat == contract.Player || AreTeammates(observation, contract.Player);
            double LeadScore(Card c)
            {
                if (Trump(c))
                    return declaringSide && unseen.Any(Trump) ? (Master(c) ? 70 : 35) - Cost(c) / 10 : -Cost(c);
                if (Master(c)) return 50 + c.GetValue();
                if (contract.GameType == GameType.Ruf && !declaringSide && observation.PublicTeammate < 0 && c.Suit == contract.Suit)
                    return 40 - Cost(c);
                return 10 - Cost(c);
            }
            return cards.OrderByDescending(x => LeadScore(x.Card)).ThenBy(x => x.Action.Id).First().Action;
        }

        var lead = GameAction.FromCardId(observation.CurrentTrick[observation.LeadingPlayer]);
        int winner = observation.LeadingPlayer;
        for (int offset = 1; offset < 4; offset++)
        {
            int seat = (observation.LeadingPlayer + offset) % 4;
            if (observation.CurrentTrick[seat] >= 0 && RankOf(GameAction.FromCardId(observation.CurrentTrick[seat]), lead) >
                RankOf(GameAction.FromCardId(observation.CurrentTrick[winner]), lead)) winner = seat;
        }
        var winningCard = GameAction.FromCardId(observation.CurrentTrick[winner]);
        if (AreTeammates(observation, winner))
        {
            bool secure = played == 3 || !unseen.Any(c => RankOf(c, lead) > RankOf(winningCard, lead));
            return secure
                ? cards.OrderByDescending(x => x.Card.GetValue()).ThenBy(x => Cost(x.Card)).ThenBy(x => x.Action.Id).First().Action
                : cards.OrderBy(x => Cost(x.Card)).ThenBy(x => x.Action.Id).First().Action;
        }
        var winners = cards.Where(x => RankOf(x.Card, lead) > RankOf(winningCard, lead)).ToArray();
        if (winners.Length > 0)
        {
            // Prefer a secure winner early in the trick; on fourth hand take it cheaply.
            var secure = winners.Where(x => !unseen.Any(c => RankOf(c, lead) > RankOf(x.Card, lead))).ToArray();
            var choices = played < 3 && secure.Length > 0 ? secure : winners;
            return choices.OrderBy(x => Cost(x.Card)).ThenBy(x => x.Action.Id).First().Action;
        }
        return cards.OrderBy(x => Cost(x.Card)).ThenBy(x => x.Action.Id).First().Action;
    }

    /// <summary>True only when partnership follows from own cards or public information.</summary>
    public static bool AreTeammates(GameObservation observation, int other)
    {
        if (other == observation.Seat) return true;
        var contract = observation.Contract;
        if (contract == null || contract.GameType == GameType.None) return false;
        if (contract.GameType != GameType.Ruf)
            return observation.Seat != contract.Player && other != contract.Player;
        int partner = observation.PublicTeammate;
        int ace = GameAction.ToCardId(new Card { Suit = contract.Suit, Rank = Rank.Ace });
        if (partner < 0 && (observation.Hand.Contains(ace) || observation.History.Any(h =>
            h.Player == observation.Seat && h.Action.Kind == ActionKind.Play && h.Action.CardId == ace))) partner = observation.Seat;
        if (partner < 0) return false;
        bool ownTeam = observation.Seat == contract.Player || observation.Seat == partner;
        return ownTeam == (other == contract.Player || other == partner);
    }
}
