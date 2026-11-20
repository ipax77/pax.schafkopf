using Microsoft.AspNetCore.SignalR;
using sk.api.Hubs;
using sk.shared;
using System.Collections.Concurrent;

namespace sk.api.Services;

public class GameHubService(IHubContext<GameHub> hub)
{
    private readonly IHubContext<GameHub> _hub = hub;
    private readonly ConcurrentDictionary<Guid, Game> _games = [];
    private readonly ConcurrentDictionary<string, Guid> _joinCodeMap = new();

    public Guid CreateNewGame(Player player)
    {
        var game = new Game();
        var guid = Guid.NewGuid();
        game.Table.Guid = guid;
        game.TryAssignEmptySeat(player);
        _games[game.Table.Guid] = game;
        var shortCode = GenerateShortCode(); // e.g., "K7RW"
        _joinCodeMap[shortCode] = game.Table.Guid;
        game.ShortCode = shortCode;
        return game.Table.Guid;
    }

    public Guid JoinGame(string gameCode, Player player)
    {
        if (_joinCodeMap.TryGetValue(gameCode, out var guid))
        {
            JoinGame(guid, player);
            return guid;
        }
        return Guid.Empty;
    }

    public void JoinGame(Guid gameId, Player player)
    {
        var game = GetGame(gameId);
        lock (game.joinLock)
        {
            if (game.HasPlayer(player))
            {
                RejoinGame(gameId, player);
                return;
            }

            if (!game.TryAssignEmptySeat(player))
                throw new InvalidOperationException("Game is full");

            game.TryStartGame();
        }
    }

    public void RejoinGame(Guid gameId, Player player)
    {
        var game = GetGame(gameId);
        lock (game.joinLock)
        {
            var seat = game.GetTablePlayer(player.Guid)
                ?? throw new InvalidOperationException("Player not part of game");
            seat.Player.ConnectionId = player.ConnectionId;
            seat.IsConnected = true;
        }
    }

    public void DisconnectPlayer(Guid gameId, Guid playerId)
    {
        var game = GetGame(gameId);
        lock (game.joinLock)
        {
            var seat = game.GetTablePlayer(playerId);
            seat?.IsConnected = false;
        }
    }

    public void LeaveGame(Guid gameId, Guid playerId)
    {
        var game = GetGame(gameId);
        lock (game.joinLock)
        {
            var seat = game.GetTablePlayer(playerId);
            seat?.IsConnected = false;
            seat?.Player.Name = string.Empty;
            seat?.Player.Guid = Guid.Empty;
            seat?.Player.ConnectionId = null;
        }
    }

    public bool Ready(Guid gameId, Guid playerId)
    {
        var game = GetGame(gameId);
        lock (game.joinLock)
        {
            if (game.GameState != GameState.Finished)
            {
                return false;
            }

            var seat = game.GetTablePlayer(playerId)
                ?? throw new InvalidOperationException("Player not in game");

            seat.ReadyForNextRound = true;

            if (game.Table.Players.All(a => a.ReadyForNextRound))
            {
                game.Reset();
                return true;
            }
            return false;
        }
    }

    public void SubmitBidding1(Guid gameId, Guid playerId, BiddingState request)
    {
        var game = GetGame(gameId);
        lock (game.joinLock)
        {
            var seat = game.GetTablePlayer(playerId) ?? throw new InvalidOperationException("Player not in game");

            game.SetBidding1(seat.Position, request);
        }
    }

    public void SubmitBidding2(Guid gameId, Guid playerId, BiddingState request)
    {
        var game = GetGame(gameId);
        lock (game.joinLock)
        {
            var seat = game.GetTablePlayer(playerId)
                ?? throw new InvalidOperationException("Player not in game");

            game.SetBidding2(seat.Position, request);
        }
    }

    public void PlayCard(Guid gameId, Guid playerId, Card card)
    {
        var game = GetGame(gameId);
        lock (game.joinLock)
        {
            var seat = game.GetTablePlayer(playerId)
                ?? throw new InvalidOperationException("Player not in game");

            game.PlayCard(seat.Position, card);
        }
    }


    public async Task BroadcastGame(Guid gameId)
    {
        var game = GetGame(gameId);
        List<(string Connection, PublicGameState State)> snapshots;
        lock (game.joinLock)
        {
            snapshots = game.Table.Players
                .Where(p => p.IsConnected && !string.IsNullOrEmpty(p.Player.ConnectionId))
                .Select(p => (p.Player.ConnectionId!, game.ToPublicGameState(p.Position))).ToList();
        }
        foreach (var (connection, state) in snapshots)
            await _hub.Clients.Client(connection).SendAsync("ReceiveGameState", state);
    }

    // Model inference happens outside the table lock. Reject results computed for an obsolete turn.
    public BotTurn GetBotTurn(Guid gameId, Guid playerId)
    {
        var game = GetGame(gameId);
        lock (game.joinLock)
        {
            var seat = game.GetTablePlayer(playerId) ?? throw new InvalidOperationException("Player not in game");
            return new(game.Revision, game.Observe(seat.Position));
        }
    }

    public bool TryApplyBotAction(Guid gameId, Guid playerId, long revision, GameAction action)
    {
        var game = GetGame(gameId);
        lock (game.joinLock)
        {
            var seat = game.GetTablePlayer(playerId) ?? throw new InvalidOperationException("Player not in game");
            if (game.Revision != revision || game.ActivePlayer != seat.Position) return false;
            game.Step(seat.Position, action);
            return true;
        }
    }

    private Game GetGame(Guid id) =>
        _games.TryGetValue(id, out var g)
            ? g
            : throw new InvalidOperationException("Game not found");

    public void AttachConnection(Guid gameId, Guid playerId, string connectionId)
    {
        var game = GetGame(gameId);
        lock (game.joinLock)
        {
            var seat = game.GetTablePlayer(playerId);
            if (seat != null)
            {
                seat.IsConnected = true;
                seat.Player.ConnectionId = connectionId;
            }
        }
    }

    private static string GenerateShortCode()
    {
        // Generate a 4-5 character string (avoiding confusing letters like O vs 0 or I vs 1)
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Repeat(chars, 4)
            .Select(s => s[Random.Shared.Next(s.Length)]).ToArray());
    }
}

public sealed record BotTurn(long Revision, GameObservation Observation);
