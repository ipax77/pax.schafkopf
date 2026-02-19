namespace sk.shared;

public partial class PublicGameState
{
    public List<GameType> GetValidGameModes()
    {
        if (!YourPosition.HasValue)
        {
            return [];
        }
        var hand = Table.Players[YourPosition.Value].Hand;
        var oberCount = hand.Count(c => c.Rank == Rank.Ober);
        var unterCount = hand.Count(c => c.Rank == Rank.Unter);

        if (oberCount + unterCount == 8)
        {
            return [GameType.Sie];
        }

        List<GameType> validGameTypes = [
            GameType.Ruf,
            GameType.Wenz,
            GameType.Solo
        ];

        if (Bidding1Result != null && Bidding1Result.InterestedPlayers.Count > 1)
        {
            validGameTypes.Remove(GameType.Ruf);
            validGameTypes.Add(GameType.None);
        }
        else if (Bidding1Result != null && Bidding1Result.InterestedPlayers.Count == 1)
        {
            var nonTrumpCards = hand.Where(s => !s.IsTrump(GameType.Ruf, Suit.Herz));
            var aces = nonTrumpCards.Where(x => x.Rank == Rank.Ace).Select(s => s.Suit).ToHashSet();
            var callableSuits = nonTrumpCards.Select(s => s.Suit).ToHashSet();
            callableSuits.RemoveWhere(x => aces.Contains(x));
            if (callableSuits.Count == 0)
            {
                validGameTypes.Remove(GameType.Ruf);
            }
        }

        var isLastPlayer = IsLastInterestedPlayer();
        var noBiddingsYet = Bidding2Result == null || Bidding2Result.GameType == GameType.None;
        if (isLastPlayer && noBiddingsYet)
        {
            validGameTypes.Remove(GameType.None);
        }

        return validGameTypes;
    }

    public List<Suit> GetValidSuits(GameType gameType)
    {
        if (!YourPosition.HasValue)
        {
            return [];
        }
        var hand = Table.Players[YourPosition.Value].Hand;

        if (gameType == GameType.Ruf)
        {
            var nonTrumpCards = hand
                .Where(c => !c.IsTrump(GameType.Ruf, Suit.Herz))
                .ToList();

            var suitsInHand = nonTrumpCards
                .Select(c => c.Suit)
                .ToHashSet();

            var aceSuitsInHand = nonTrumpCards
                .Where(c => c.Rank == Rank.Ace)
                .Select(c => c.Suit)
                .ToHashSet();

            suitsInHand.ExceptWith(aceSuitsInHand);
            return suitsInHand.ToList();
        }

        List<Suit> validSuits = [
            Suit.Eichel,
            Suit.Gras,
            Suit.Herz,
            Suit.Schellen
        ];

        return validSuits;
    }

    public List<Card> GetValidCards()
    {
        if (Bidding2Result == null || !YourPosition.HasValue)
        {
            return [];
        }

        if (ActivePlayer != YourPosition.Value)
        {
            return [];
        }

        var firstPlayer = LeadingPlayer;
        var firstCard = Table.CurrentTrick[firstPlayer];
        var hand = Table.Players[ActivePlayer].Hand;
        var rufAce = Bidding2Result.GameType == GameType.Ruf
            ? new Card() { Rank = Rank.Ace, Suit = Bidding2Result.Suit } : null;

        if (firstCard == null)
        {
            if (rufAce != null && hand.Contains(rufAce))
            {
                var rufSuitCards = hand
                    .Where(x => !x.IsTrump(Bidding2Result.GameType, Bidding2Result.Suit)
                        && x.Suit == rufAce.Suit && x.Rank != Rank.Ace)
                    .ToList();
                if (rufSuitCards.Count < 3)
                {
                    return hand.Except(rufSuitCards).ToList();
                }
                else
                {
                    return hand;
                }
            }
            else
            {
                return hand;
            }
        }

        var trumpCards = hand
            .Where(x => x.IsTrump(Bidding2Result.GameType, Bidding2Result.Suit))
            .ToList();
        var suitCards = hand
            .Where(x => x.Suit == firstCard.Suit)
            .Except(trumpCards)
            .ToList();

        if (firstCard.IsTrump(Bidding2Result.GameType, Bidding2Result.Suit))
        {
            return trumpCards.Count > 0 ? trumpCards :
                rufAce == null || DrunterDurch ? hand : hand.Except([rufAce]).ToList();
        }
        else if (rufAce != null && firstCard.Suit == rufAce.Suit && hand.Contains(rufAce))
        {
            return [rufAce];
        }
        else
        {
            return suitCards.Count > 0 ? suitCards :
                rufAce == null || DrunterDurch ? hand : hand.Except([rufAce]).ToList();
        }
    }

    private bool IsLastInterestedPlayer()
    {
        if (Bidding1Result == null || !YourPosition.HasValue)
        {
            return false;
        }
        int playerIndex = YourPosition.Value;
        var interestedPlayers = Bidding1Result.InterestedPlayers;
        if (interestedPlayers.Count == 1)
        {
            return interestedPlayers[0] == playerIndex;
        }
        var orderedInterestedPlayers = interestedPlayers
            .OrderBy(p => DistanceFromLeader(p, LeadingPlayer))
            .ToList();
        return orderedInterestedPlayers.Last() == playerIndex;
    }

    private static int DistanceFromLeader(int player, int leader)
    {
        return (player - leader + 4) % 4;
    }
}
