using sk.shared;

namespace sk.tests;

[TestClass]
public sealed class CountTests
{
    [TestMethod]
    public void CanCountHand1()
    {
        List<Card> hand = [new Card() { Rank = Rank.Ace, Suit = Suit.Eichel }];
        var count = hand.Sum(s => s.GetValue());
        Assert.AreEqual(11, count);
    }

    [TestMethod]
    public void CanCountHand2()
    {
        List<Card> hand = [
            new Card() { Rank = Rank.Ace, Suit = Suit.Eichel },
            new Card() { Rank = Rank.Ten, Suit = Suit.Eichel },
            new Card() { Rank = Rank.King, Suit = Suit.Eichel },
            new Card() { Rank = Rank.Ober, Suit = Suit.Eichel },
            new Card() { Rank = Rank.Unter, Suit = Suit.Eichel },
            new Card() { Rank = Rank.Eight, Suit = Suit.Eichel },
            new Card() { Rank = Rank.Nine, Suit = Suit.Eichel },
        ];
        var expected = 11 + 10 + 4 + 3 + 2 + 0 + 0;
        var count = hand.Sum(s => s.GetValue());
        Assert.AreEqual(expected, count);
    }
}
