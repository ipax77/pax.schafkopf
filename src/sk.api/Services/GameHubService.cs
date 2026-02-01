using Microsoft.AspNetCore.SignalR;
using sk.api.Hubs;
using sk.shared;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace sk.api.Services
{
    public class GameHubService
    {
        private readonly IHubContext<GameHub> _hubContext;
        private readonly ConcurrentDictionary<Guid, Game> _games = new();

        public GameHubService(IHubContext<GameHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public async Task<Guid> CreateNewGame()
        {
            var game = new Game();
            game.Table.Guid = Guid.NewGuid();
            _games[game.Table.Guid] = game;
            game.DealCards();
            game.GameState = GameState.Bidding1;
            game.ActivePlayer = 0;

            var publicGameState = game.ToPublicGameState();
            await _hubContext.Clients.All.SendAsync("ReceiveGameState", publicGameState);

            return game.Table.Guid;
        }

        public async Task JoinGame(Guid gameId, Player player, string connectionId)
        {
            if (_games.TryGetValue(gameId, out var game))
            {
                var exisiting = game.Table.Players.FirstOrDefault(f => f.Player.Guid == player.Guid);
                if (exisiting is not null)
                {
                    await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveGameState", game.ToPublicGameState());
                    return;
                }
                var playerSlot = game.Table.Players.FirstOrDefault(p => string.IsNullOrEmpty(p.Player.Name));
                if (playerSlot != null)
                {
                    playerSlot.Player = player;
                    var publicGameState = game.ToPublicGameState();
                    await _hubContext.Clients.All.SendAsync("ReceiveGameState", publicGameState);
                }
            }
        }
    }
}