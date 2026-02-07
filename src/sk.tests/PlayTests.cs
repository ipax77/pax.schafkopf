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
}
