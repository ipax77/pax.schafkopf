using Microsoft.AspNetCore.SignalR;
using Moq;
using sk.api.Hubs;
using sk.api.Services;
using sk.shared;

namespace sk.tests;

[TestClass]
public class GameHubServiceTests
{
    private Mock<IHubContext<GameHub>> _mockHubContext = null!;
    private Mock<IHubClients> _mockHubClients = null!;
    private Mock<IClientProxy> _mockAllClientsProxy = null!; // For All and AllExcept
    private Mock<ISingleClientProxy> _mockSingleClientProxy = null!; // For Client(connectionId)
    private GameHubService _gameHubService = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _mockHubContext = new Mock<IHubContext<GameHub>>();
        _mockHubClients = new Mock<IHubClients>();
        _mockAllClientsProxy = new Mock<IClientProxy>();
        _mockSingleClientProxy = new Mock<ISingleClientProxy>();

        _mockHubContext.Setup(h => h.Clients).Returns(_mockHubClients.Object);
        _mockHubClients.Setup(c => c.All).Returns(_mockAllClientsProxy.Object);
        _mockHubClients
            .Setup(c => c.AllExcept(It.IsAny<IReadOnlyList<string>>()))
            .Returns(_mockAllClientsProxy.Object);
        _mockHubClients
            .Setup(c => c.Client(It.IsAny<string>()))
            .Returns(_mockSingleClientProxy.Object);

        _gameHubService = new GameHubService(_mockHubContext.Object);
    }
    [TestMethod]
    public async Task CreateGame_ShouldBroadcastPersonalizedAndSpectatorState()
    {
        var player = new Player { Guid = Guid.NewGuid(), Name = "Player1" };
        var connectionId = "conn1";

        var gameId = _gameHubService.CreateNewGame(player);
        _gameHubService.AttachConnection(gameId, player.Guid, connectionId);

        await _gameHubService.BroadcastGame(gameId);

        _mockHubClients.Verify(c => c.Client(connectionId), Times.Once);
        _mockSingleClientProxy.Verify(p =>
            p.SendCoreAsync(
                "ReceiveGameState",
                It.Is<object?[]>(o => o.Length == 1 && o[0] is PublicGameState),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // _mockHubClients.Verify(c =>
        //     c.AllExcept(It.Is<IReadOnlyList<string>>(l => l.Contains(connectionId))),
        //     Times.Once);
    }

    [TestMethod]
    public async Task JoinGame_ShouldAddSecondPlayerAndBroadcast()
    {
        var p1 = new Player { Guid = Guid.NewGuid(), Name = "P1" };
        var p2 = new Player { Guid = Guid.NewGuid(), Name = "P2" };

        var gId = _gameHubService.CreateNewGame(p1);
        _gameHubService.AttachConnection(gId, p1.Guid, "c1");

        _gameHubService.JoinGame(gId, p2);
        _gameHubService.AttachConnection(gId, p2.Guid, "c2");

        await _gameHubService.BroadcastGame(gId);

        _mockHubClients.Verify(c => c.Client("c1"), Times.Once);
        _mockHubClients.Verify(c => c.Client("c2"), Times.Once);

        _mockSingleClientProxy.Verify(p =>
            p.SendCoreAsync(
                "ReceiveGameState",
                It.Is<object?[]>(o => o[0] is PublicGameState),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [TestMethod]
    public async Task RejoinGame_ShouldRestoreConnectionAndBroadcast()
    {
        var player = new Player { Guid = Guid.NewGuid(), Name = "Player" };
        var gameId = _gameHubService.CreateNewGame(player);

        _gameHubService.AttachConnection(gameId, player.Guid, "oldConn");
        _gameHubService.DisconnectPlayer(gameId, player.Guid);

        _gameHubService.RejoinGame(gameId, player);
        _gameHubService.AttachConnection(gameId, player.Guid, "newConn");

        await _gameHubService.BroadcastGame(gameId);

        _mockHubClients.Verify(c => c.Client("newConn"), Times.Once);
    }

    [TestMethod]
    public async Task Disconnect_ShouldExcludePlayerFromPersonalizedBroadcast()
    {
        var player = new Player { Guid = Guid.NewGuid(), Name = "Player" };
        var gameId = _gameHubService.CreateNewGame(player);

        _gameHubService.AttachConnection(gameId, player.Guid, "conn");
        _gameHubService.DisconnectPlayer(gameId, player.Guid);

        await _gameHubService.BroadcastGame(gameId);

        _mockHubClients.Verify(c => c.Client(It.IsAny<string>()), Times.Never);
        // _mockHubClients.Verify(c => c.AllExcept(It.IsAny<IReadOnlyList<string>>()), Times.Once);
    }

}