namespace sk.shared;

/// <summary>Rules shared by server validation, UI hints, and headless policies.</summary>
public static class GameRules
{
    public static List<Card> LegalCards(IReadOnlyList<Card> hand, Bidding2Result contract, Card? lead, bool ranAway)
    {
        if (hand.Count == 0) return [];
        var type = contract.GameType;
        var suit = contract.Suit;
        var ace = type == GameType.Ruf ? new Card { Suit = suit, Rank = Rank.Ace } : null;
        if (lead == null)
        {
            if (ace != null && hand.Contains(ace) && !ranAway)
            {
                var called = hand.Where(c => c.Suit == suit && !c.IsTrump(type, suit)).ToList();
                if (called.Count < 4) return hand.Where(c => !called.Contains(c) || c == ace).ToList();
            }
            return [.. hand];
        }
        var following = hand.Where(c => lead.IsTrump(type, suit)
            ? c.IsTrump(type, suit) : !c.IsTrump(type, suit) && c.Suit == lead.Suit).ToList();
        if (following.Count > 0)
        {
            if (!ranAway && ace != null && !lead.IsTrump(type, suit) && lead.Suit == suit && hand.Contains(ace)) return [ace];
            return following;
        }
        // The called ace is locked until searched/run away, except on the last trick.
        return ace != null && !ranAway && hand.Count > 1 ? hand.Where(c => c != ace).ToList() : [.. hand];
    }

    public static List<Suit> CallableSuits(IReadOnlyList<Card> hand) =>
        new[] { Suit.Schellen, Suit.Gras, Suit.Eichel }.Where(s =>
            hand.Any(c => c.Suit == s && !c.IsTrump(GameType.Ruf, s)) &&
            !hand.Contains(new Card { Suit = s, Rank = Rank.Ace })).ToList();

    public static bool HasSie(IReadOnlyList<Card> hand) => hand.Count == 8 && hand.Distinct().Count() == 8 &&
        hand.All(c => (c.Rank is Rank.Ober or Rank.Unter) && c.Suit is >= Suit.Schellen and <= Suit.Eichel);

    public static List<GameAction> LegalBids(IReadOnlyList<Card> hand, bool canPass)
    {
        List<GameAction> actions = [];
        if (canPass) actions.Add(GameAction.Pass);
        if (HasSie(hand)) { actions.Add(GameAction.Bid(GameType.Sie)); return actions; }
        foreach (var s in CallableSuits(hand)) actions.Add(GameAction.Bid(GameType.Ruf, s));
        actions.Add(GameAction.Bid(GameType.Wenz));
        actions.Add(GameAction.Bid(GameType.Wenz, tout: true));
        foreach (var s in new[] { Suit.Schellen, Suit.Herz, Suit.Gras, Suit.Eichel })
        {
            actions.Add(GameAction.Bid(GameType.Solo, s));
            actions.Add(GameAction.Bid(GameType.Solo, s, true));
        }
        return actions;
    }

    public static int BidRank(GameType type, bool tout) => type switch
    {
        GameType.Sie => 6, GameType.Solo => tout ? 5 : 3, GameType.Wenz => tout ? 4 : 2,
        GameType.Ruf => 1, _ => 0
    };
}
