using Microsoft.AspNetCore.SignalR;
using Moq;
using sk.api.Hubs;
using sk.api.Services;
using sk.shared;

namespace sk.tests
{
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
            _mockHubClients.Setup(c => c.AllExcept(It.IsAny<IReadOnlyList<string>>())).Returns(_mockAllClientsProxy.Object);
            _mockHubClients.Setup(c => c.Client(It.IsAny<string>())).Returns(_mockSingleClientProxy.Object);

            _gameHubService = new GameHubService(_mockHubContext.Object);
        }

        [TestMethod]
        public async Task CreateNewGame_ShouldCreateGameAndBroadcastState()
        {
            // Act
            var gameId = await _gameHubService.CreateNewGame();

            // Assert
            Assert.AreNotEqual(Guid.Empty, gameId);
            _mockHubClients.Verify(c => c.AllExcept(It.Is<IReadOnlyList<string>>(l => l.Count == 0)), Times.Once);
            _mockAllClientsProxy.Verify(p => p.SendCoreAsync(
                "ReceiveGameState",
                It.Is<object?[]>(o => o != null && o.Length == 1 && o[0] is PublicGameState),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [TestMethod]
        public async Task JoinGame_NewPlayer_ShouldAddPlayerAndBroadcast()
        {
            // Arrange
            var gameId = await _gameHubService.CreateNewGame();
            var player = new Player { Guid = Guid.NewGuid(), Name = "Test Player" };
            var connectionId = "test_connection_id";

            // Act
            await _gameHubService.JoinGame(gameId, player, connectionId);

            // Assert
            _mockHubClients.Verify(c => c.Client(connectionId), Times.Once);
            _mockSingleClientProxy.Verify(p => p.SendCoreAsync(
                "ReceiveGameState",
                It.Is<object?[]>(o => o != null && o.Length == 1 && o[0] is PublicGameState),
                It.IsAny<CancellationToken>()),
                Times.Once);

            _mockHubClients.Verify(c => c.AllExcept(It.Is<IReadOnlyList<string>>(l => l.Contains(connectionId))), Times.Once);
            _mockAllClientsProxy.Verify(p => p.SendCoreAsync(
                "ReceiveGameState",
                It.Is<object?[]>(o => o != null && o.Length == 1 && o[0] is PublicGameState),
                It.IsAny<CancellationToken>()),
                Times.Exactly(2)); // Once for CreateNewGame, once for JoinGame (AllExcept for others)
        }

        [TestMethod]
        public async Task JoinGame_ExistingPlayer_ShouldUpdateConnectionAndSendPersonalizedState()
        {
            // Arrange
            var gameId = await _gameHubService.CreateNewGame();
            var player = new Player { Guid = Guid.NewGuid(), Name = "Test Player" };
            var firstConnectionId = "first_connection";
            var secondConnectionId = "second_connection";
            await _gameHubService.JoinGame(gameId, player, firstConnectionId);

            // Reset mocks to clear previous calls before verifying the second join
            _mockHubClients.Invocations.Clear();
            _mockSingleClientProxy.Invocations.Clear();
            _mockAllClientsProxy.Invocations.Clear();


            // Act
            await _gameHubService.JoinGame(gameId, player, secondConnectionId);

            // Assert
            _mockHubClients.Verify(c => c.Client(secondConnectionId), Times.Once);
            _mockSingleClientProxy.Verify(p => p.SendCoreAsync(
                "ReceiveGameState",
                It.Is<object?[]>(o => o != null && o.Length == 1 && o[0] is PublicGameState),
                It.IsAny<CancellationToken>()),
                Times.Once);

            _mockHubClients.Verify(c => c.AllExcept(It.IsAny<IReadOnlyList<string>>()), Times.Once); // Should broadcast spectator state
        }

        [TestMethod]
        public async Task DisconnectPlayer_ShouldMarkPlayerAsDisconnectedAndBroadcast()
        {
            // Arrange
            var gameId = await _gameHubService.CreateNewGame();
            var player = new Player { Guid = Guid.NewGuid(), Name = "Test Player" };
            var connectionId = "test_connection_id";
            await _gameHubService.JoinGame(gameId, player, connectionId);

            // Reset mocks to clear previous calls
            _mockHubClients.Invocations.Clear();
            _mockSingleClientProxy.Invocations.Clear();
            _mockAllClientsProxy.Invocations.Clear();

            // Act
            await _gameHubService.DisconnectPlayer(connectionId);

            // Assert
            _mockHubClients.Verify(c => c.AllExcept(It.Is<IReadOnlyList<string>>(l => l.Count == 0)), Times.Once); // Spectator state
            _mockAllClientsProxy.Verify(p => p.SendCoreAsync(
                "ReceiveGameState",
                It.Is<object?[]>(o => o != null && o.Length == 1 && o[0] is PublicGameState),
                It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }
}
