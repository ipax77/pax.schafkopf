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
        private readonly ConcurrentDictionary<Guid, string> _playerConnections = new();

        public GameHubService(IHubContext<GameHub> hubContext)
        {
            _hubContext = hubContext;
        }

        private async Task BroadcastGameState(Game game)
        {
            var playerConnectionIds = new List<string>();

            // Send personalized state to each player and collect their connection IDs
            foreach (var player in game.Table.Players)
            {
                if (player.Player.Guid != Guid.Empty && _playerConnections.TryGetValue(player.Player.Guid, out var connectionId))
                {
                    playerConnectionIds.Add(connectionId);
                    var playerState = game.ToPublicGameState(player.Position);
                    await _hubContext.Clients.Client(connectionId).SendAsync("ReceiveGameState", playerState);
                }
            }

            // Send spectator state to all other clients
            var spectatorState = game.ToPublicGameState();
            await _hubContext.Clients.AllExcept(playerConnectionIds).SendAsync("ReceiveGameState", spectatorState);
        }

        public async Task<Guid> CreateNewGame()
        {
            var game = new Game();
            game.Table.Guid = Guid.NewGuid();
            _games[game.Table.Guid] = game;
            game.DealCards();
            game.GameState = GameState.Bidding1;
            game.ActivePlayer = 0;

            await BroadcastGameState(game);

            return game.Table.Guid;
        }

        public async Task JoinGame(Guid gameId, Player player, string connectionId)
        {
            if (_games.TryGetValue(gameId, out var game))
            {
                var exisiting = game.Table.Players.FirstOrDefault(f => f.Player.Guid == player.Guid);
                if (exisiting is not null)
                {
                    exisiting.IsConnected = true;
                    _playerConnections[player.Guid] = connectionId;
                    await BroadcastGameState(game);
                    return;
                }
                var playerSlot = game.Table.Players.FirstOrDefault(p => string.IsNullOrEmpty(p.Player.Name));
                if (playerSlot != null)
                {
                    playerSlot.Player = player;
                    playerSlot.IsConnected = true;
                    _playerConnections[player.Guid] = connectionId;
                    await BroadcastGameState(game);
                }
            }
        }

        public async Task DisconnectPlayer(string connectionId)
        {
            Guid playerId = Guid.Empty;
            foreach (var entry in _playerConnections)
            {
                if (entry.Value == connectionId)
                {
                    playerId = entry.Key;
                    break;
                }
            }

            if (playerId != Guid.Empty)
            {
                // now find the game this player is in
                foreach (var game in _games.Values)
                {
                    var playerInGame = game.Table.Players.FirstOrDefault(p => p.Player.Guid == playerId);
                    if (playerInGame != null)
                    {
                        playerInGame.IsConnected = false;
                        _playerConnections.TryRemove(playerId, out _); // remove from connections
                        await BroadcastGameState(game);
                        break; // assuming player can only be in one game
                    }
                }
            }
        }
    }
}