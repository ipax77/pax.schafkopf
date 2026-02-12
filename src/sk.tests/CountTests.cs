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

    [TestMethod]
    public void CanCountRunners()
    {
        List<Card> hand = [
            new Card() { Rank = Rank.Ober, Suit = Suit.Eichel },
            new Card() { Rank = Rank.Ober, Suit = Suit.Gras },
            new Card() { Rank = Rank.Ober, Suit = Suit.Herz },
            new Card() { Rank = Rank.Eight, Suit = Suit.Herz },
            new Card() { Rank = Rank.Eight, Suit = Suit.Eichel },
            new Card() { Rank = Rank.Eight, Suit = Suit.Gras },
            new Card() { Rank = Rank.Eight, Suit = Suit.Schellen },
        ];

        Game game = new();
        
        game.GameState = GameState.Bidding1;
        game.Table.Players[0].Hand = hand;
        game.Table.Players[0].StartingHand = hand;

        game.SetBidding1(0, new() { WouldPlay = true });
        game.SetBidding1(1, new() { WouldPlay = false });
        game.SetBidding1(2, new() { WouldPlay = false });
        game.SetBidding1(3, new() { WouldPlay = false });

        game.SetBidding2(0, new() { WouldPlay = true, ProposedGame = GameType.Solo, ProposedSuit = Suit.Herz });

        game.CreateGameResult();
        Assert.IsNotEmpty(game.GameResults);
        Assert.AreEqual(3, game.GameResults[0].Runners);
    }

    [TestMethod]
    public void CanCountRunnersWithPartner()
    {
        List<Card> hand1 = [
            new Card() { Rank = Rank.Ober, Suit = Suit.Eichel },
            new Card() { Rank = Rank.Ober, Suit = Suit.Gras },
            new Card() { Rank = Rank.Unter, Suit = Suit.Gras },
            new Card() { Rank = Rank.Nine, Suit = Suit.Herz },
            new Card() { Rank = Rank.Nine, Suit = Suit.Eichel },
            new Card() { Rank = Rank.Nine, Suit = Suit.Gras },
            new Card() { Rank = Rank.Nine, Suit = Suit.Schellen },
            new Card() { Rank = Rank.Eight, Suit = Suit.Eichel },
        ];

        List<Card> hand2 = [
            new Card() { Rank = Rank.Ober, Suit = Suit.Herz },
            new Card() { Rank = Rank.Unter, Suit = Suit.Herz },
            new Card() { Rank = Rank.Eight, Suit = Suit.Herz },
            new Card() { Rank = Rank.Eight, Suit = Suit.Eichel },
            new Card() { Rank = Rank.Eight, Suit = Suit.Gras },
            new Card() { Rank = Rank.Seven, Suit = Suit.Eichel },
            new Card() { Rank = Rank.Seven, Suit = Suit.Gras },
            new Card() { Rank = Rank.Ace, Suit = Suit.Schellen },
        ];

        var otherCards = PlayTests.GetOtherCards([.. hand1, ..hand2]).Chunk(8).ToList();

        Game game = new();
        game.GameState = GameState.Bidding1;
        game.Table.Players[0].Hand = hand1;
        game.Table.Players[0].StartingHand = [..hand1];
        game.Table.Players[1].Hand = hand2;
        game.Table.Players[1].StartingHand = [..hand2];
        game.Table.Players[2].Hand = otherCards[0].ToList();
        game.Table.Players[2].StartingHand = otherCards[0].ToList();
        game.Table.Players[3].Hand = otherCards[1].ToList();
        game.Table.Players[3].StartingHand = otherCards[1].ToList();


        game.SetBidding1(0, new() { WouldPlay = true });
        game.SetBidding1(1, new() { WouldPlay = false });
        game.SetBidding1(2, new() { WouldPlay = false });
        game.SetBidding1(3, new() { WouldPlay = false });

        game.SetBidding2(0, new() { WouldPlay = true, ProposedGame = GameType.Ruf, ProposedSuit = Suit.Schellen });

        for (int t = 0; t < 8; t++)
        {
            for (int i = 0; i < 4; i++)
            {
                int current = game.ActivePlayer;
                var player = game.Table.Players[current];
                var validCards = player.GetValidCards(game).ToList();
                Assert.IsNotEmpty(validCards);
                var card = validCards[0];
                game.PlayCard(current, card);
            }
        }
        Assert.AreEqual(GameState.Finished, game.GameState);
        Assert.IsNotEmpty(game.GameResults);
        Assert.AreEqual(3, game.GameResults[0].Runners);
    }
}
