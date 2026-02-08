namespace sk.shared;

public class PublicGameState
{
    public string ShortCode { get; set; } = string.Empty;
    public GameState GameState { get; set; }
    public int LeadingPlayer { get; set; }
    public int ActivePlayer { get; set; }
    public int? YourPosition { get; set; }
    public int Turn { get; set; }
    public Bidding1Result? Bidding1Result { get; set; }
    public Bidding2Result? Bidding2Result { get; set; }
    public bool DrunterDurch { get; set; }
    public int PublicTeammate { get; set; } = -1;
    public Table Table { get; set; } = new();
    public PublicGameResult? PublicGameResult { get; set; }
}

public sealed class PublicGameResult
{
    public List<List<Card>> StartingHands { get; set; } = [];
    public List<int> PlayerCashes { get; set; } = [];
    public GameResult GameResult { get; set; } = new();
}

public static class PublicGameStateExtensions
{
    public static List<GameType> GetValidGameModes(this PublicGameState publicGameState)
    {
        if (!publicGameState.YourPosition.HasValue)
        {
            return [];
        }
        var hand = publicGameState.Table.Players[publicGameState.YourPosition.Value].Hand;
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

        if (publicGameState.Bidding1Result != null && publicGameState.Bidding1Result.InterestedPlayers.Count > 1)
        {
            validGameTypes.Remove(GameType.Ruf);
            validGameTypes.Add(GameType.None);
        }
        else if (publicGameState.Bidding1Result != null && publicGameState.Bidding1Result.InterestedPlayers.Count == 0)
        {
            var nonTrumpCards = hand.Where(s => !s.IsTrump(GameType.Ruf, Suit.Herz));
            var aces = nonTrumpCards.Where(x => x.Rank == Rank.Ace).Select(s => s.Suit).ToHashSet();
            var callableSuits = nonTrumpCards.Select(s => s.Suit).ToHashSet();
            callableSuits.RemoveWhere(x => !aces.Contains(x));
            if (callableSuits.Count == 0)
            {
                validGameTypes.Remove(GameType.Ruf);
            }
        }

        var isLastPlayer = (publicGameState.Bidding1Result?.InterestedPlayers.Last() ?? -1)
            == publicGameState.YourPosition.Value;
        var noBiddingsYet = publicGameState.Bidding2Result == null || publicGameState.Bidding2Result.GameType == GameType.None;
        if (isLastPlayer && noBiddingsYet && validGameTypes.Contains(GameType.None))
        {
            validGameTypes.Remove(GameType.None);
        }

        return validGameTypes;
    }

    public static List<Suit> GetValidSuits(this PublicGameState publicGameState, GameType gameType)
    {
        if (!publicGameState.YourPosition.HasValue)
        {
            return [];
        }
        var hand = publicGameState.Table.Players[publicGameState.YourPosition.Value].Hand;


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

    public static List<Card> GetValidCards(this PublicGameState publicGameState)
    {
        if (publicGameState.Bidding2Result == null || !publicGameState.YourPosition.HasValue)
        {
            return [];
        }

        if (publicGameState.ActivePlayer != publicGameState.YourPosition.Value)
        {
            return [];
        }

        var firstPlayer = publicGameState.LeadingPlayer;
        var firstCard = publicGameState.Table.CurrentTrick[firstPlayer];
        var hand = publicGameState.Table.Players[publicGameState.ActivePlayer].Hand;
        var rufAce = publicGameState.Bidding2Result.GameType == GameType.Ruf
            ? new Card() { Rank = Rank.Ace, Suit = publicGameState.Bidding2Result.Suit } : null;

        if (firstCard == null)
        {
            if (rufAce != null && hand.Contains(rufAce))
            {
                var rufSuitCards = hand
                    .Where(x => !x.IsTrump(publicGameState.Bidding2Result.GameType, publicGameState.Bidding2Result.Suit)
                        && x.Suit == rufAce.Suit && x.Rank != Rank.Ace)
                    .ToList();
                if (rufSuitCards.Count >= 3)
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
            .Where(x => x.IsTrump(publicGameState.Bidding2Result.GameType, publicGameState.Bidding2Result.Suit))
            .ToList();
        var suitCards = hand
            .Where(x => x.Suit == firstCard.Suit)
            .Except(trumpCards)
            .ToList();

        if (firstCard.IsTrump(publicGameState.Bidding2Result.GameType, publicGameState.Bidding2Result.Suit))
        {
            return trumpCards.Count > 0 ? trumpCards :
                rufAce == null || publicGameState.DrunterDurch ? hand : hand.Except([rufAce]).ToList();
        }
        else if (rufAce != null && firstCard.Suit == rufAce.Suit && hand.Contains(rufAce))
        {
            return [rufAce];
        }
        else
        {
            return suitCards.Count > 0 ? suitCards : 
                rufAce == null || publicGameState.DrunterDurch ? hand : hand.Except([rufAce]).ToList();
        }
    }

    public static string GetGameTypeString(this Bidding2Result bidding2Result)
    {
        var gameTypeString = bidding2Result.GameType switch
        {
            GameType.Sie => "Sie",
            GameType.Wenz => "Wenz",
            GameType.Ruf => bidding2Result.Suit switch
            {
                Suit.Schellen => "Auf die Lumberte",
                Suit.Eichel => "Auf die Alte",
                Suit.Gras => "Auf die Blaue",
                _ => string.Empty
            },
            GameType.Solo => bidding2Result.Suit switch
            {
                Suit.Schellen => "Schellen Solo",
                Suit.Eichel => "Eichel Solo",
                Suit.Gras => "Gras Solo",
                Suit.Herz => "Herz Solo",
                _ => string.Empty
            },
            _ => string.Empty
        };

        if (bidding2Result.Tout)
        {
            gameTypeString += " Tout";
        }

        return gameTypeString;
    }
}