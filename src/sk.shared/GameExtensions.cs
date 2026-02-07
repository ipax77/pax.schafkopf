namespace sk.shared;

public static class GameExtensions
{
    public static bool TryAssignEmptySeat(this Game game, Player player)
    {
        var emptySlot = game.Table.Players.OrderBy(o => o.Position).FirstOrDefault(f => f.Player.Guid == Guid.Empty);
        if (emptySlot is null)
        {
            return false;
        }
        emptySlot.Player = player;
        emptySlot.IsConnected = true;
        return true;
    }

    public static bool HasPlayer(this Game game, Player player)
    {
        var slot = game.Table.Players.FirstOrDefault(f => f.Player.Guid == player.Guid);
        return slot is not null;
    }

    public static bool TryStartGame(this Game game)
    {
        if (!game.Table.Players.All(a => a.IsConnected))
        {
            return false;
        }
        game.DealCards();
        game.GameState = GameState.Bidding1;
        return true;
    }

    public static TablePlayer? GetTablePlayer(this Game game, Guid playerId)
    {
        return game.Table.Players.FirstOrDefault(f => f.Player.Guid == playerId);
    }

    public static PublicGameState ToPublicGameState(this Game game, int forPlayer)
    {
        var publicTable = new Table
        {
            Guid = game.Table.Guid,
            PreviouseTrick = game.Table.PreviouseTrick,
            CurrentTrick = game.Table.CurrentTrick,
            CurrentTrickCard = game.Table.CurrentTrickCard,
        };

        for (int i = 0; i < 4; i++)
        {
            var originalPlayer = game.Table.Players[i];
            publicTable.Players[i] = new TablePlayer
            {
                Player = originalPlayer.Player,
                Position = originalPlayer.Position,
                Tricks = originalPlayer.Tricks,
                Hand = i == forPlayer ? originalPlayer.Hand : GetPrivateHand(originalPlayer.Hand),
                IsConnected = originalPlayer.IsConnected
            };
        }

        return new PublicGameState
        {
            ShortCode = game.ShortCode,
            GameState = game.GameState,
            LeadingPlayer = game.leadingPlayer,
            ActivePlayer = game.ActivePlayer,
            YourPosition = forPlayer,
            Turn = game.Turn,
            Bidding1Result = game.GetPublicBidding1Result(),
            Bidding2Result = game.GetPublicBidding2Result(),
            Table = publicTable
        };
    }

    private static List<Card> GetPrivateHand(List<Card> hand)
    {
        List<Card> cards = [];
        foreach (var card in hand)
        {
            cards.Add(new() { Rank = Rank.Seven, Suit = Suit.None });
        }
        return cards;
    }

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

    public static void CollectTrick(this Game game)
    {
        ArgumentNullException.ThrowIfNull(game.Bidding2Result);

        var trick = new List<(int Player, Card Card)>();

        int player = game.leadingPlayer;

        for (int i = 0; i < 4; i++)
        {
            var card = game.Table.CurrentTrick[player];
            ArgumentNullException.ThrowIfNull(card);

            trick.Add((player, card));
            player = (player + 1) % 4;
        }

        var firstCard = trick[0].Card;
        var comparer = new TrickCardComparer(game.Bidding2Result.GameType, game.Bidding2Result.Suit, firstCard);
        var winner = trick.OrderByDescending(o => o.Card, comparer).First();

        // store trick in play order, not sorted order
        foreach (var (_, card) in trick)
            game.Table.Players[winner.Player].Tricks.Add(card);

        game.Table.PreviouseTrick = game.Table.CurrentTrick;
        game.Table.CurrentTrick = [null, null, null, null];

        game.ActivePlayer = winner.Player;
    }

    public static void CreateGameResult(this Game game)
    {
        var bidding = game.Bidding2Result;
        if (bidding is null)
            return;

        var gameType = bidding.GameType;
        var suit = bidding.Suit;

        var declarer = game.Table.Players[bidding.PlayerIndex];
        var hand = new List<Card>(declarer.StartingHand);

        TablePlayer? partner = null;

        if (gameType == GameType.Ruf)
        {
            var rufAce = new Card { Rank = Rank.Ace, Suit = suit };
            partner = game.Table.Players
                .FirstOrDefault(p => p.StartingHand.Contains(rufAce));

            if (partner is not null)
                hand.AddRange(partner.StartingHand);
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

        int playerPoints = hand.Sum(c => c.GetValue());
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

        var defenders = game.Table.Players
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

        game.GameResults.Add(result);
        game.GameState = GameState.Finished;
    }


    public static void DealCards(this Game game)
    {
        var deck = CreateDeck().Shuffle();
        var hands = deck.Chunk(8).ToArray();

        for (int i = 0; i < 4; i++)
        {
            game.Table.Players[i].Hand = [.. hands[i]];
            game.Table.Players[i].StartingHand = [.. hands[i]];
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
}

public static class GameResultExtensions
{
    private static readonly int rufCost = 10;
    private static readonly int soloCost = 20;
    private static readonly int runnerCost = 10;
    private static readonly int schneiderCost = 10;
    private static readonly int schwarzCost = 10;

    public static void SetGameCost(this GameResult gameResult)
    {
        int cost = gameResult.GameType == GameType.Ruf
            ? rufCost
            : soloCost;

        bool hasRunnerBonus =
            gameResult.Runners >= 3 ||
            (gameResult.GameType == GameType.Wenz && gameResult.Runners >= 2);

        if (hasRunnerBonus)
            cost += gameResult.Runners * runnerCost;

        if (gameResult.PlayerPoints is < 30 or > 90)
            cost += schneiderCost;

        if (gameResult.PlayerPoints is 0 or 120)
            cost += schwarzCost;

        if (gameResult.Tout)
            cost *= 2;

        if (gameResult.GameType == GameType.Sie)
            cost *= 2;

        gameResult.Cost = cost;
    }

}