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
    }

}
