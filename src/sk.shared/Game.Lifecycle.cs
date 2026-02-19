namespace sk.shared;

public partial class Game
{
    private static readonly Rank[] Ranks =
    [
        Rank.Seven, Rank.Eight, Rank.Nine,
        Rank.Unter, Rank.Ober,
        Rank.King, Rank.Ten, Rank.Ace
    ];

    private static readonly Suit[] Suits =
    [
        Suit.Schellen, Suit.Herz, Suit.Gras, Suit.Eichel
    ];

    private static readonly List<Card> stdRunnerCards = [
        new Card() { Rank = Rank.Ober, Suit = Suit.Eichel },
        new Card() { Rank = Rank.Ober, Suit = Suit.Gras },
        new Card() { Rank = Rank.Ober, Suit = Suit.Herz },
        new Card() { Rank = Rank.Ober, Suit = Suit.Schellen },
        new Card() { Rank = Rank.Unter, Suit = Suit.Eichel },
        new Card() { Rank = Rank.Unter, Suit = Suit.Gras },
        new Card() { Rank = Rank.Unter, Suit = Suit.Herz },
        new Card() { Rank = Rank.Unter, Suit = Suit.Schellen },
    ];

    private static readonly List<Card> wenzRunnerCards = [
        new Card() { Rank = Rank.Unter, Suit = Suit.Eichel },
        new Card() { Rank = Rank.Unter, Suit = Suit.Gras },
        new Card() { Rank = Rank.Unter, Suit = Suit.Herz },
        new Card() { Rank = Rank.Unter, Suit = Suit.Schellen },
    ];

    public bool TryStartGame()
    {
        if (!Table.Players.All(a => a.IsConnected))
        {
            return false;
        }
        DealCards();
        GameState = GameState.Bidding1;
        return true;
    }

    public void DealCards()
    {
        var deck = CreateDeck().Shuffle();
        var hands = deck.Chunk(8).ToArray();

        for (int i = 0; i < 4; i++)
        {
            Table.Players[i].Hand = [.. hands[i]];
            Table.Players[i].StartingHand = [.. hands[i]];
        }
    }

    private static Card[] CreateDeck()
    {
        var deck = new Card[32];
        int i = 0;

        foreach (var suit in Suits)
            foreach (var rank in Ranks)
                deck[i++] = new Card { Rank = rank, Suit = suit };

        return deck;
    }

    public void CreateGameResult()
    {
        var bidding = Bidding2Result;
        if (bidding is null)
        {
            GameResults.Add(new());
            return;
        }

        var gameType = bidding.GameType;
        var suit = bidding.Suit;

        var declarer = Table.Players[bidding.PlayerIndex];
        var hand = new List<Card>(declarer.StartingHand);
        var trickCards = new List<Card>(declarer.Tricks);

        TablePlayer? partner = null;

        if (PublicTeammate >= 0)
        {
            partner = Table.Players[PublicTeammate];
            hand.AddRange(partner.StartingHand);
            trickCards.AddRange(partner.Tricks);
        }

        var runnerCards = gameType == GameType.Wenz
            ? wenzRunnerCards
            : stdRunnerCards;

        // Sort once by trump order
        hand.Sort((a, b) =>
            b.GetCardOrder(gameType, suit)
             .CompareTo(a.GetCardOrder(gameType, suit)));

        int runners = 0;
        for (int i = 0; i < runnerCards.Count && i < hand.Count; i++)
        {
            if (runnerCards[i].Equals(hand[i]))
                runners++;
            else
                break;
        }

        int playerPoints = trickCards.Sum(c => c.GetValue());
        bool declarerWins = bidding.Tout ? playerPoints == 120 : playerPoints > 60;

        var result = new GameResult
        {
            GameType = gameType,
            Suit = suit,
            Tout = bidding.Tout,
            Player = declarer.Player,
            Player2 = partner?.Player,
            Runners = runners,
            PlayerPoints = playerPoints
        };

        result.SetGameCost();

        List<TablePlayer> declarers = partner is null
            ? [declarer]
            : [declarer, partner];

        var defenders = Table.Players
            .Except(declarers)
            .ToList();

        if (partner is null)
        {
            foreach (var d in defenders)
                d.Cash += declarerWins ? -result.Cost : result.Cost;

            declarer.Cash += declarerWins ? 3 * result.Cost : -3 * result.Cost;
        }
        else
        {
            // Ruf: 2 vs 2
            foreach (var p in declarers)
                p.Cash += declarerWins ? result.Cost : -result.Cost;

            foreach (var d in defenders)
                d.Cash += declarerWins ? -result.Cost : result.Cost;
        }

        GameResults.Add(result);
        GameState = GameState.Finished;
    }

    public void Reset()
    {
        Turn = 0;
        ReadyCheck = 0;
        Table.Reset();
        _bidding1Decisions[0] = null;
        _bidding1Decisions[1] = null;
        _bidding1Decisions[2] = null;
        _bidding1Decisions[3] = null;
        _bidding2States.Clear();
        Bidding1Result = null;
        Bidding2Result = null;
        DrunterDurch = false;
        PublicTeammate = -1;
        Dealer = (Dealer + 1) % 4;
        ActivePlayer = (Dealer + 1) % 4;
        DealCards();
        GameState = GameState.Bidding1;
    }
}
