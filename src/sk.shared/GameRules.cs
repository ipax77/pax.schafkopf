namespace sk.shared;

/// <summary>Rules shared by server validation, UI hints, and headless policies.</summary>
public static class GameRules
{
    public static List<Card> LegalCards(IReadOnlyList<Card> hand, Bidding2Result contract, Card? lead, bool ranAway)
    {
        var result = new List<Card>(hand.Count);
        var type = contract.GameType;
        var suit = contract.Suit;
        bool lockAce = type == GameType.Ruf && !ranAway;
        if (lead == null)
        {
            bool hasAce = false;
            int calledCount = 0;
            if (lockAce)
                for (int i = 0; i < hand.Count; i++)
                {
                    var card = hand[i];
                    if (card.Suit != suit || card.IsTrump(type, suit)) continue;
                    calledCount++;
                    hasAce |= card.Rank == Rank.Ace;
                }
            for (int i = 0; i < hand.Count; i++)
            {
                var card = hand[i];
                if (hasAce && calledCount < 4 && card.Suit == suit && card.Rank != Rank.Ace && !card.IsTrump(type, suit)) continue;
                result.Add(card);
            }
            return result;
        }
        bool trumpLed = lead.IsTrump(type, suit);
        Card? calledAce = null;
        for (int i = 0; i < hand.Count; i++)
        {
            var card = hand[i];
            bool trump = card.IsTrump(type, suit);
            if (trumpLed ? trump : !trump && card.Suit == lead.Suit) result.Add(card);
            if (lockAce && card.Suit == suit && card.Rank == Rank.Ace) calledAce = card;
        }
        if (result.Count > 0)
        {
            if (calledAce != null && !trumpLed && lead.Suit == suit)
            {
                result.Clear();
                result.Add(calledAce);
            }
            return result;
        }
        for (int i = 0; i < hand.Count; i++)
        {
            var card = hand[i];
            if (lockAce && hand.Count > 1 && card.Suit == suit && card.Rank == Rank.Ace) continue;
            result.Add(card);
        }
        return result;
    }

    public static List<Suit> CallableSuits(IReadOnlyList<Card> hand)
    {
        int present = 0, aces = 0;
        for (int i = 0; i < hand.Count; i++)
        {
            var card = hand[i];
            if (card.Suit is not (Suit.Schellen or Suit.Gras or Suit.Eichel) || card.IsTrump(GameType.Ruf, card.Suit)) continue;
            present |= 1 << (int)card.Suit;
            if (card.Rank == Rank.Ace) aces |= 1 << (int)card.Suit;
        }
        var suits = new List<Suit>(3);
        int callable = present & ~aces;
        for (int s = 1; s <= 4; s++) if ((callable & (1 << s)) != 0) suits.Add((Suit)s);
        return suits;
    }

    public static bool HasSie(IReadOnlyList<Card> hand)
    {
        if (hand.Count != 8) return false;
        uint seen = 0;
        for (int i = 0; i < hand.Count; i++)
        {
            var card = hand[i];
            if (card.Rank is not (Rank.Ober or Rank.Unter) || card.Suit is < Suit.Schellen or > Suit.Eichel) return false;
            uint bit = 1u << GameAction.ToCardId(card);
            if ((seen & bit) != 0) return false;
            seen |= bit;
        }
        return true;
    }

    public static List<GameAction> LegalBids(IReadOnlyList<Card> hand, bool canPass)
    {
        var actions = new List<GameAction>(14);
        if (canPass) actions.Add(GameAction.Pass);
        if (HasSie(hand)) { actions.Add(GameAction.Bid(GameType.Sie)); return actions; }
        foreach (var s in CallableSuits(hand)) actions.Add(GameAction.Bid(GameType.Ruf, s));
        actions.Add(GameAction.Bid(GameType.Wenz));
        actions.Add(GameAction.Bid(GameType.Wenz, tout: true));
        for (int s = 1; s <= 4; s++)
        {
            actions.Add(GameAction.Bid(GameType.Solo, (Suit)s));
            actions.Add(GameAction.Bid(GameType.Solo, (Suit)s, true));
        }
        return actions;
    }

    public static int BidRank(GameType type, bool tout) => type switch
    {
        GameType.Sie => 6, GameType.Solo => tout ? 5 : 3, GameType.Wenz => tout ? 4 : 2,
        GameType.Ruf => 1, _ => 0
    };
}
