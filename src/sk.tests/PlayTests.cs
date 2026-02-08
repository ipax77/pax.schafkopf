using sk.shared;

namespace sk.tests;

[TestClass]
public sealed class PlayTests
{
    [TestMethod]
    public void CanCreateLobby()
    {
        Game game = new();

        var testPlayers = GetTestPlayers();

        var result1 = game.TryAssignEmptySeat(testPlayers[0]);
        Assert.IsTrue(result1);
        var result2 = game.TryAssignEmptySeat(testPlayers[1]);
        Assert.IsTrue(result2);
        var result3 = game.TryAssignEmptySeat(testPlayers[2]);
        Assert.IsTrue(result3);
        var result4 = game.TryAssignEmptySeat(testPlayers[3]);
        Assert.IsTrue(result4);

        var publicGameState4 = game.ToPublicGameState(3);
        Assert.IsTrue(publicGameState4.YourPosition.HasValue);
        Assert.AreEqual(3, publicGameState4.YourPosition);
    }

    [TestMethod]
    public void CanPlayCards()
    {
        Game game = new();

        var testPlayers = GetTestPlayers();
        game.TryAssignEmptySeat(testPlayers[0]);
        game.TryAssignEmptySeat(testPlayers[1]);
        game.TryAssignEmptySeat(testPlayers[2]);
        game.TryAssignEmptySeat(testPlayers[3]);

        game.TryStartGame();

        game.SetBidding1(0, new() { WouldPlay = true });
        game.SetBidding1(1, new() { WouldPlay = false });
        game.SetBidding1(2, new() { WouldPlay = false });
        game.SetBidding1(3, new() { WouldPlay = false });

        game.SetBidding2(0, new() { WouldPlay = true, ProposedGame = GameType.Ruf, ProposedSuit = Suit.Schellen });

        for (int i = 0; i < 4; i++)
        {
            int current = game.ActivePlayer;
            var player = game.Table.Players[current];
            var card = player.GetValidCards(game).First();

            game.PlayCard(current, card);
        }

        var publicGameState = game.ToPublicGameState(0);
        Assert.IsTrue(publicGameState.Table.CurrentTrick.All(a => a == null));
    }

    [TestMethod]
    public void CanAssignSeats()
    {
        Game game = new();

        var testPlayers = GetTestPlayers();
        game.TryAssignEmptySeat(testPlayers[0]);
        game.TryAssignEmptySeat(testPlayers[1]);
        game.TryAssignEmptySeat(testPlayers[2]);
        game.TryAssignEmptySeat(testPlayers[3]);

        game.TryStartGame();

        var publicGameState = game.ToPublicGameState(3);
        Assert.IsTrue(publicGameState.YourPosition.HasValue);
        Assert.AreEqual(3, publicGameState.YourPosition.Value);
        var playersByView = GetPlayersByView(publicGameState);
        var firstViewPlayer = playersByView[0];
        Assert.AreEqual(3, firstViewPlayer.ServerIndex);
        Assert.AreEqual(0, firstViewPlayer.ViewIndex);
    }

    [TestMethod]
    public void CanCreateResult()
    {
        Game game = new();

        var testPlayers = GetTestPlayers();
        game.TryAssignEmptySeat(testPlayers[0]);
        game.TryAssignEmptySeat(testPlayers[1]);
        game.TryAssignEmptySeat(testPlayers[2]);
        game.TryAssignEmptySeat(testPlayers[3]);

        game.TryStartGame();

        game.SetBidding1(0, new() { WouldPlay = true });
        game.SetBidding1(1, new() { WouldPlay = false });
        game.SetBidding1(2, new() { WouldPlay = false });
        game.SetBidding1(3, new() { WouldPlay = false });

        game.SetBidding2(0, new() { WouldPlay = true, ProposedGame = GameType.Solo, ProposedSuit = Suit.Herz });

        for (int t = 0; t < 8; t++)
        {
            for (int i = 0; i < 4; i++)
            {
                int current = game.ActivePlayer;
                var player = game.Table.Players[current];
                var card = player.GetValidCards(game).First();
                game.PlayCard(current, card);
            }
        }
        Assert.HasCount(1, game.GameResults);
        var result = game.GameResults[0];
        Assert.AreEqual(GameType.Solo, result.GameType);
        Assert.AreEqual(Suit.Herz, result.Suit);
        Assert.IsNotNull(result.Player);
        Assert.IsTrue(result.PlayerPoints is >= 0 and <= 120);
        Assert.IsGreaterThan(0, result.Cost);
        var totalCashChange = game.Table.Players.Sum(p => p.Cash);
        Assert.AreEqual(0, totalCashChange, "Cash must balance to zero.");
    }

    [TestMethod]
    public void CanCreateResult2()
    {
        Game game = new();

        var testPlayers = GetTestPlayers();
        game.TryAssignEmptySeat(testPlayers[0]);
        game.TryAssignEmptySeat(testPlayers[1]);
        game.TryAssignEmptySeat(testPlayers[2]);
        game.TryAssignEmptySeat(testPlayers[3]);

        game.TryStartGame();
        DealTestHand(game);

        game.SetBidding1(0, new() { WouldPlay = true });
        game.SetBidding1(1, new() { WouldPlay = false });
        game.SetBidding1(2, new() { WouldPlay = false });
        game.SetBidding1(3, new() { WouldPlay = false });

        game.SetBidding2(0, new() { WouldPlay = true, ProposedGame = GameType.Solo, ProposedSuit = Suit.Herz });

        for (int t = 0; t < 8; t++)
        {
            for (int i = 0; i < 4; i++)
            {
                int current = game.ActivePlayer;
                var player = game.Table.Players[current];
                var card = player.GetValidCards(game).First();
                game.PlayCard(current, card);
            }
        }
        Assert.HasCount(1, game.GameResults);
        var result = game.GameResults[0];
        Assert.AreEqual(GameType.Solo, result.GameType);
        Assert.AreEqual(Suit.Herz, result.Suit);
        Assert.IsNotNull(result.Player);
        Assert.IsTrue(result.PlayerPoints is >= 0 and <= 120);
        Assert.IsGreaterThan(0, result.Cost);
        Assert.AreEqual(3, result.Runners);
        var totalCashChange = game.Table.Players.Sum(p => p.Cash);
        Assert.AreEqual(0, totalCashChange, "Cash must balance to zero.");
    }

    private static Player[] GetTestPlayers()
    {
        return [
            new Player() { Name = "Test1 ", Guid = Guid.NewGuid(), ConnectionId = "Test1" },
            new Player() { Name = "Test2 ", Guid = Guid.NewGuid(), ConnectionId = "Test2" },
            new Player() { Name = "Test3 ", Guid = Guid.NewGuid(), ConnectionId = "Test3" },
            new Player() { Name = "Test4 ", Guid = Guid.NewGuid(), ConnectionId = "Test4" },
        ];
    }

    private static List<PlayerViewInfo> GetPlayersByView(PublicGameState publicGameState)
    {
        if (publicGameState.YourPosition.HasValue)
        {
            return publicGameState.Table.Players
                .Select((p, serverIndex) => new PlayerViewInfo(p, serverIndex, (serverIndex - publicGameState.YourPosition.Value + 4) % 4))
                .OrderBy(p => p.ViewIndex)
                .ToList();
        }
        else
        {
            return publicGameState.Table.Players
                .Select((p, serverIndex) => new PlayerViewInfo(p, serverIndex, serverIndex))
                .OrderBy(p => p.ViewIndex)
                .ToList();
        }
    }

    private static void DealTestHand(Game game)
    {
        var soloHand = GetStrongHerzSoloHand();
        var otherCards = GetOtherCards(soloHand).Shuffle().Chunk(8).ToList();

        game.Table.Players[0].Hand = soloHand;
        game.Table.Players[1].Hand = otherCards[0].ToList();
        game.Table.Players[2].Hand = otherCards[1].ToList();
        game.Table.Players[3].Hand = otherCards[2].ToList();

        game.Table.Players[0].StartingHand = [.. game.Table.Players[0].Hand];
        game.Table.Players[1].StartingHand = [.. game.Table.Players[1].Hand];
        game.Table.Players[2].StartingHand = [.. game.Table.Players[2].Hand];
        game.Table.Players[3].StartingHand = [.. game.Table.Players[3].Hand];
    }

    private static List<Card> GetStrongHerzSoloHand()
    {
        return [
            new Card() { Rank = Rank.Ober, Suit = Suit.Eichel },
            new Card() { Rank = Rank.Ober, Suit = Suit.Gras },
            new Card() { Rank = Rank.Ober, Suit = Suit.Herz },
            new Card() { Rank = Rank.Unter, Suit = Suit.Gras },
            new Card() { Rank = Rank.Ace, Suit = Suit.Herz },
            new Card() { Rank = Rank.Ten, Suit = Suit.Herz },
            new Card() { Rank = Rank.Ace, Suit = Suit.Eichel },
            new Card() { Rank = Rank.Seven, Suit = Suit.Schellen },
        ];
    }

    private static List<Card> GetOtherCards(List<Card> cards)
    {
        var deck = CreateDeck();
        return deck.Except(cards).ToList();
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
}
