using sk.shared;

namespace sk.tests;

[TestClass]
public sealed class GameTests
{
    [TestMethod]
    public void CanDealCards()
    {
        Game game = new();

        game.DealCards();

        foreach (var player in game.Table.Players)
        {
            Assert.HasCount(8, player.Hand);
        }
    }

    [TestMethod]
    public void DealCards_AllCardsAreUnique()
    {
        var game = new Game();
        game.DealCards();

        var allCards = game.Table.Players
            .SelectMany(p => p.Hand)
            .ToList();

        var distinctCards = allCards
            .Select(c => (c.Rank, c.Suit))
            .Distinct()
            .Count();

        Assert.AreEqual(32, distinctCards);
    }

    [TestMethod]
    public void CanDetectTrump()
    {
        Card card = new() { Rank = Rank.Unter, Suit = Suit.Schellen };
        var isTrump = card.IsTrump(GameType.Wenz, Suit.None);
        Assert.IsTrue(isTrump);
    }

    [TestMethod]
    public void CanDoBidding1()
    {
        Game game = new();
        game.GameState = GameState.Bidding1;

        game.SetBidding1(0, new() { WouldPlay = true });
        game.SetBidding1(1, new() { WouldPlay = false });
        game.SetBidding1(2, new() { WouldPlay = false });
        game.SetBidding1(3, new() { WouldPlay = false });

        Assert.AreEqual(GameState.Bidding2, game.GameState);
        Assert.AreEqual(0, game.ActivePlayer);
    }

    [TestMethod]
    public void CanDoBidding2()
    {
        Game game = new();
        game.GameState = GameState.Bidding1;

        game.SetBidding1(0, new() { WouldPlay = true });
        game.SetBidding1(1, new() { WouldPlay = false });
        game.SetBidding1(2, new() { WouldPlay = false });
        game.SetBidding1(3, new() { WouldPlay = false });

        game.SetBidding2(0, new() { WouldPlay = true, ProposedGame = GameType.Ruf, ProposedSuit = Suit.Schellen });
        Assert.AreEqual(GameState.Playing, game.GameState);
        Assert.AreEqual(0, game.ActivePlayer);
    }

    [TestMethod]
    public void CanDoTwoPlayerBidding2()
    {
        Game game = new();
        game.GameState = GameState.Bidding1;

        game.SetBidding1(0, new() { WouldPlay = true });
        game.SetBidding1(1, new() { WouldPlay = true });
        game.SetBidding1(2, new() { WouldPlay = false });
        game.SetBidding1(3, new() { WouldPlay = false });

        game.SetBidding2(0, new() { WouldPlay = false });
        game.SetBidding2(1, new() { WouldPlay = true, ProposedGame = GameType.Wenz });
        Assert.AreEqual(GameState.Playing, game.GameState);
        Assert.AreEqual(GameType.Wenz, game.Bidding2Result?.GameType);
        Assert.AreEqual(0, game.ActivePlayer);
    }

    [TestMethod]
    public void CanDoThreePlayerBidding2()
    {
        Game game = new();
        game.GameState = GameState.Bidding1;

        game.SetBidding1(0, new() { WouldPlay = true });
        game.SetBidding1(1, new() { WouldPlay = true });
        game.SetBidding1(2, new() { WouldPlay = true });
        game.SetBidding1(3, new() { WouldPlay = false });

        game.SetBidding2(0, new() { WouldPlay = false });
        game.SetBidding2(1, new() { WouldPlay = true, ProposedGame = GameType.Wenz });
        game.SetBidding2(2, new() { WouldPlay = true, ProposedGame = GameType.Solo, ProposedSuit = Suit.Herz });
        Assert.AreEqual(GameState.Playing, game.GameState);
        Assert.AreEqual(GameType.Solo, game.Bidding2Result?.GameType);
        Assert.AreEqual(0, game.ActivePlayer);
    }

    [TestMethod]
    public void CanDoFourPlayerBidding2()
    {
        Game game = new();
        game.GameState = GameState.Bidding1;

        game.SetBidding1(0, new() { WouldPlay = true });
        game.SetBidding1(1, new() { WouldPlay = true });
        game.SetBidding1(2, new() { WouldPlay = true });
        game.SetBidding1(3, new() { WouldPlay = true });

        game.SetBidding2(0, new() { WouldPlay = false });
        game.SetBidding2(1, new() { WouldPlay = true, ProposedGame = GameType.Wenz });
        game.SetBidding2(2, new() { WouldPlay = true, ProposedGame = GameType.Solo, ProposedSuit = Suit.Herz });
        game.SetBidding2(3, new() { WouldPlay = true, ProposedGame = GameType.Solo, ProposedSuit = Suit.Schellen, Tout = true });
        Assert.AreEqual(GameState.Playing, game.GameState);
        Assert.AreEqual(GameType.Solo, game.Bidding2Result?.GameType);
        Assert.AreEqual(Suit.Schellen, game.Bidding2Result?.Suit);
        Assert.AreEqual(0, game.ActivePlayer);
    }

    [TestMethod]
    public void CanPlayCard()
    {
        Game game = new();
        game.DealCards();
        game.GameState = GameState.Bidding1;

        game.SetBidding1(0, new() { WouldPlay = true });
        game.SetBidding1(1, new() { WouldPlay = false });
        game.SetBidding1(2, new() { WouldPlay = false });
        game.SetBidding1(3, new() { WouldPlay = false });

        game.SetBidding2(0, new() { WouldPlay = true, ProposedGame = GameType.Ruf, ProposedSuit = Suit.Schellen });
        var card = game.Table.Players[0].Hand[0];
        game.PlayCard(0, card);

        Assert.HasCount(7, game.Table.Players[0].Hand);
        Assert.IsNotNull(game.Table.CurrentTrick[0]);
    }


    [TestMethod]
    public void CanCollectTrick()
    {
        Game game = new();
        game.DealCards();
        game.GameState = GameState.Bidding1;

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

        Assert.IsTrue(game.Table.Players.Any(p => p.Tricks.Count == 4));
        Assert.IsTrue(game.Table.PreviouseTrick!.All(c => c != null));
        Assert.IsTrue(game.Table.CurrentTrick.All(c => c == null));
        var winnerPlayer = game.Table.Players.First(f => f.Tricks.Count > 0);
        Assert.AreEqual(winnerPlayer.Position, game.ActivePlayer);
    }
}
