using sk.shared;

namespace sk.tests;

[TestClass]
public sealed class ScoringRegressionTests
{
    [TestMethod]
    [DataRow(30, 2, false, false, 30)]
    [DataRow(31, 2, false, false, 20)]
    [DataRow(60, 4, false, false, 20)]
    [DataRow(61, 4, false, true, 20)]
    [DataRow(90, 6, false, true, 20)]
    [DataRow(91, 6, false, true, 30)]
    [DataRow(120, 7, false, true, 30)]
    [DataRow(120, 8, false, true, 40)]
    [DataRow(0, 1, false, false, 30)]
    [DataRow(0, 0, false, false, 40)]
    [DataRow(120, 7, true, false, 40)]
    [DataRow(120, 8, true, true, 40)]
    public void ScoringUsesAsymmetricSchneiderAndTrickCounts(int points, int tricks, bool tout, bool wins, int cost)
    {
        var r = new GameResult { GameType = GameType.Solo, PlayerPoints = points, PlayerTricks = tricks, Tout = tout };
        r.SetGameCost();
        Assert.AreEqual(wins, r.DeclarerWins);
        Assert.AreEqual(cost, r.Cost);
        Assert.AreEqual(tricks is 0 or 8, r.Schwarz);
    }

    [TestMethod]
    public void ToutLosingZeroPointTrickLosesMoneyAndSettlesOnlyOnce()
    {
        var g = new Game();
        g.ResetEpisode(42);
        var deck = Enumerable.Range(0, 32).Select(GameAction.FromCardId).ToArray();
        var zeroTrick = deck.Where(c => c.GetValue() == 0).Take(4).ToList();
        g.GameState = GameState.Playing;
        g.Bidding2Result = new() { PlayerIndex = 0, GameType = GameType.Solo, Suit = Suit.Herz, Tout = true };
        g.Turn = 8;
        foreach (var p in g.Table.Players) p.Hand.Clear();
        g.Table.Players[0].Tricks = deck.Except(zeroTrick).ToList();
        g.Table.Players[1].Tricks = zeroTrick;
        g.CreateGameResult();
        Assert.AreEqual(120, g.GameResults[0].PlayerPoints);
        Assert.AreEqual(7, g.GameResults[0].PlayerTricks);
        Assert.IsFalse(g.GameResults[0].DeclarerWins);
        Assert.IsLessThan(0, g.Table.Players[0].Cash);
        Assert.AreEqual(0, g.Table.Players.Sum(p => p.Cash));
        var cash = g.Table.Players.Select(p => p.Cash).ToArray();
        Assert.Throws<InvalidOperationException>(g.CreateGameResult);
        CollectionAssert.AreEqual(cash, g.Table.Players.Select(p => p.Cash).ToArray());
    }

    [TestMethod]
    public void RunnersCountEitherSideAndContinueBeyondUnters()
    {
        var deck = Enumerable.Range(0, 32).Select(GameAction.FromCardId).ToArray();
        var trumps = deck.Where(c => c.IsTrump(GameType.Ruf, Suit.Gras))
            .OrderByDescending(c => c.GetCardRank(GameType.Ruf, Suit.Gras)).ToArray();
        Assert.AreEqual((10, false), GameScoring.CountRunners(trumps.Take(10).ToArray(), GameType.Ruf, Suit.Gras));
        Assert.AreEqual((10, true), GameScoring.CountRunners(deck.Except(trumps.Take(10)).ToArray(), GameType.Ruf, Suit.Gras));
        var unters = deck.Where(c => c.Rank == Rank.Unter).OrderByDescending(c => c.Suit).ToArray();
        Assert.AreEqual((2, false), GameScoring.CountRunners(unters.Take(2).ToArray(), GameType.Wenz, Suit.None));
        Assert.AreEqual((4, true), GameScoring.CountRunners(deck.Except(unters).ToArray(), GameType.Wenz, Suit.None));
        var result = new GameResult { GameType = GameType.Wenz, PlayerPoints = 61, PlayerTricks = 4, Runners = 2, WithoutRunners = true };
        result.SetGameCost();
        Assert.AreEqual(40, result.Cost);
    }
}
