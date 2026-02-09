using sk.shared;

namespace sk.tests;

[TestClass]
public sealed class BiddingTests
{
    [TestMethod]
    public void CanMakeBidding1Public()
    {
        Game game = new();
        game.GameState = GameState.Bidding1;
        game.SetBidding1(0, new() { WouldPlay = true });

        var publicState = game.ToPublicGameState(1);
        Assert.AreEqual(1, publicState.YourPosition);
        Assert.IsNotNull(publicState.Bidding1Result);
        Assert.HasCount(1, publicState.Bidding1Result.InterestedPlayers);
    }

    [TestMethod]
    public void CanMakeTwoPlayerBidding1Public()
    {
        Game game = new();
        game.GameState = GameState.Bidding1;
        game.SetBidding1(0, new() { WouldPlay = true });
        game.SetBidding1(1, new() { WouldPlay = true });

        var publicState = game.ToPublicGameState(2);
        Assert.IsNotNull(publicState.Bidding1Result);
        Assert.Contains(0, publicState.Bidding1Result.InterestedPlayers);
        Assert.Contains(1, publicState.Bidding1Result.InterestedPlayers);
    }

    [TestMethod]
    public void CanMakeBidding2Public()
    {
        Game game = new();
        game.GameState = GameState.Bidding1;

        game.SetBidding1(0, new() { WouldPlay = true });
        game.SetBidding1(1, new() { WouldPlay = false });
        game.SetBidding1(2, new() { WouldPlay = false });
        game.SetBidding1(3, new() { WouldPlay = false });

        Assert.AreEqual(GameState.Bidding2, game.GameState);

        var publicState = game.ToPublicGameState(0);

        var validGameModes = publicState.GetValidGameModes();
        Assert.IsNotEmpty(validGameModes);
    }

    [TestMethod]
    public void CanMakeTwoPlayerBidding2Public()
    {
        Game game = new();
        game.GameState = GameState.Bidding1;

        game.SetBidding1(0, new() { WouldPlay = true });
        game.SetBidding1(1, new() { WouldPlay = true });
        game.SetBidding1(2, new() { WouldPlay = false });
        game.SetBidding1(3, new() { WouldPlay = false });

        var publicState0 = game.ToPublicGameState(0);
        var validGameModes0 = publicState0.GetValidGameModes();
        Assert.Contains(GameType.None, validGameModes0);

        game.SetBidding2(0, new() { WouldPlay = false });
        var publicState1 = game.ToPublicGameState(1);
        var validGameModes1 = publicState1.GetValidGameModes();
        Assert.IsNotEmpty(validGameModes1);
        Assert.DoesNotContain(GameType.None, validGameModes1);
    }

    [TestMethod]
    public void CanMakeThreePlayerBidding2Public()
    {
        Game game = new();
        game.GameState = GameState.Bidding1;

        game.SetBidding1(0, new() { WouldPlay = true });
        game.SetBidding1(1, new() { WouldPlay = true });
        game.SetBidding1(2, new() { WouldPlay = true });
        game.SetBidding1(3, new() { WouldPlay = false });

        game.SetBidding2(0, new() { WouldPlay = false });
        game.SetBidding2(1, new() { WouldPlay = true, ProposedGame = GameType.Wenz });

        var publicState = game.ToPublicGameState(2);
        var validGameModes = publicState.GetValidGameModes();
        Assert.Contains(GameType.None, validGameModes);
    }

    [TestMethod]
    public void CanMakeFourPlayerBidding2Public()
    {
        Game game = new();
        game.GameState = GameState.Bidding1;

        game.SetBidding1(0, new() { WouldPlay = true });
        game.SetBidding1(1, new() { WouldPlay = true });
        game.SetBidding1(2, new() { WouldPlay = true });
        game.SetBidding1(3, new() { WouldPlay = true });

        game.SetBidding2(0, new() { WouldPlay = false });
        game.SetBidding2(1, new() { WouldPlay = true, ProposedGame = GameType.Wenz });
        game.SetBidding2(2, new() { WouldPlay = true, ProposedGame = GameType.Solo, ProposedSuit = Suit.Herz, Tout = true });

        var publicState = game.ToPublicGameState(3);
        Assert.IsNotNull(publicState.Bidding2Result);
        var validGameModes = publicState.GetValidGameModes();
        Assert.Contains(GameType.None, validGameModes);
    }

    [TestMethod]
    public void CanPlayPublicCard()
    {
        Game game = new();
        game.DealCards();
        game.GameState = GameState.Bidding1;

        game.SetBidding1(0, new() { WouldPlay = true });
        game.SetBidding1(1, new() { WouldPlay = false });
        game.SetBidding1(2, new() { WouldPlay = false });
        game.SetBidding1(3, new() { WouldPlay = false });

        game.SetBidding2(0, new() { WouldPlay = true, ProposedGame = GameType.Ruf, ProposedSuit = Suit.Schellen });

        Assert.AreEqual(GameState.Playing, game.GameState);

        var publicState = game.ToPublicGameState(0);
        Assert.IsTrue(publicState.YourPosition.HasValue);
        Assert.AreEqual(publicState.ActivePlayer, publicState.YourPosition.Value);

        var validCards = publicState.GetValidCards();
        Assert.IsNotEmpty(validCards);
    }

    [TestMethod]
    public void CanSelectSuitForBidding2()
    {
        Game game = new();
        game.GameState = GameState.Bidding1;

        game.SetBidding1(0, new() { WouldPlay = true });
        game.SetBidding1(1, new() { WouldPlay = false });
        game.SetBidding1(2, new() { WouldPlay = false });
        game.SetBidding1(3, new() { WouldPlay = false });

        Assert.AreEqual(GameState.Bidding2, game.GameState);

        var publicState = game.ToPublicGameState(0);
        publicState.Table.Players[0].Hand = [
            new() { Rank = Rank.Ober, Suit = Suit.Gras },
            new() { Rank = Rank.Ober, Suit = Suit.Herz },
            new() { Rank = Rank.Ober, Suit = Suit.Schellen },
            new() { Rank = Rank.Ace, Suit = Suit.Herz },
            new() { Rank = Rank.Ten, Suit = Suit.Herz },
            new() { Rank = Rank.Ten, Suit = Suit.Eichel },
            new() { Rank = Rank.King, Suit = Suit.Gras },
            new() { Rank = Rank.Eight, Suit = Suit.Gras },
        ];
        var validSuits = publicState.GetValidSuits(GameType.Ruf);

        Assert.Contains(Suit.Eichel, validSuits);
        Assert.Contains(Suit.Gras, validSuits);
    }

    [TestMethod]
    public void CanDeteckedRufLockedForBidding2()
    {
        Game game = new();
        game.GameState = GameState.Bidding1;

        game.SetBidding1(0, new() { WouldPlay = true });
        game.SetBidding1(1, new() { WouldPlay = false });
        game.SetBidding1(2, new() { WouldPlay = false });
        game.SetBidding1(3, new() { WouldPlay = false });

        Assert.AreEqual(GameState.Bidding2, game.GameState);

        var publicState = game.ToPublicGameState(0);
        publicState.Table.Players[0].Hand = [
            new() { Rank = Rank.Ober, Suit = Suit.Gras },
            new() { Rank = Rank.Ober, Suit = Suit.Herz },
            new() { Rank = Rank.Ober, Suit = Suit.Schellen },
            new() { Rank = Rank.Ace, Suit = Suit.Herz },
            new() { Rank = Rank.Ten, Suit = Suit.Herz },
            new() { Rank = Rank.Ace, Suit = Suit.Eichel },
            new() { Rank = Rank.King, Suit = Suit.Eichel },
            new() { Rank = Rank.Eight, Suit = Suit.Eichel },
        ];

        var validGameModes = publicState.GetValidGameModes();
        Assert.DoesNotContain(GameType.Ruf, validGameModes);
        var validSuits = publicState.GetValidSuits(GameType.Ruf);
        Assert.DoesNotContain(Suit.Eichel, validSuits);
    }

    [TestMethod]
    public void CanSetTwoPlayerBidding1ValidGameModes()
    {
        Game game = new();
        game.GameState = GameState.Bidding1;
        game.SetBidding1(0, new() { WouldPlay = true });
        game.SetBidding1(1, new() { WouldPlay = true });
        game.SetBidding1(2, new() { WouldPlay = false });
        game.SetBidding1(3, new() { WouldPlay = false });

        var publicState1 = game.ToPublicGameState(0);
        var validGameModes1 = publicState1.GetValidGameModes();
        Assert.Contains(GameType.None, validGameModes1);

        game.SetBidding2(0, new() { WouldPlay = false } );
        var publicState2 = game.ToPublicGameState(1);
        var validGameModes2 = publicState2.GetValidGameModes();
        Assert.DoesNotContain(GameType.None, validGameModes2);
    }

    [TestMethod]
    public void CanSetTwoPlayerBidding1ValidGameModes2()
    {
        Game game = new();
        game.GameState = GameState.Bidding1;
        game.SetBidding1(0, new() { WouldPlay = false });
        game.SetBidding1(1, new() { WouldPlay = true });
        game.SetBidding1(2, new() { WouldPlay = false });
        game.SetBidding1(3, new() { WouldPlay = true });

        var publicState1 = game.ToPublicGameState(1);
        var validGameModes1 = publicState1.GetValidGameModes();
        Assert.Contains(GameType.None, validGameModes1);

        game.SetBidding2(1, new() { WouldPlay = false });
        var publicState2 = game.ToPublicGameState(3);
        var validGameModes2 = publicState2.GetValidGameModes();
        Assert.DoesNotContain(GameType.None, validGameModes2);
    }

    [TestMethod]
    public void CanSetTwoPlayerBidding1ValidGameModes3()
    {
        Game game = new();
        game.ActivePlayer = 2;
        game.GameState = GameState.Bidding1;
        game.SetBidding1(2, new() { WouldPlay = false });
        game.SetBidding1(3, new() { WouldPlay = true });
        game.SetBidding1(0, new() { WouldPlay = false });
        game.SetBidding1(1, new() { WouldPlay = true });

        var publicState1 = game.ToPublicGameState(3);
        var validGameModes1 = publicState1.GetValidGameModes();
        Assert.Contains(GameType.None, validGameModes1);
        game.SetBidding2(3, new() { WouldPlay = false });
        var publicState2 = game.ToPublicGameState(1);
        var validGameModes2 = publicState2.GetValidGameModes();
        Assert.DoesNotContain(GameType.None, validGameModes2);
    }
}
