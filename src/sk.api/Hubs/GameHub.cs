using Microsoft.AspNetCore.SignalR;
using sk.api.Services;
using sk.shared;

namespace sk.api.Hubs
{
    public class GameHub : Hub
    {
        private readonly GameHubService _gameHubService;

        public GameHub(GameHubService gameHubService)
        {
            _gameHubService = gameHubService;
        }

        public async Task<Guid> CreateNewGame()
        {
            return await _gameHubService.CreateNewGame();
        }

        public async Task JoinGame(Guid gameId, Player player)
        {
            await _gameHubService.JoinGame(gameId, player, Context.ConnectionId);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await _gameHubService.DisconnectPlayer(Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }
    }
}
