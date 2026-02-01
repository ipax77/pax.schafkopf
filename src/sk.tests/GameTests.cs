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

}
