using Microsoft.AspNetCore.SignalR;
using sk.api.Hubs;
using sk.shared;
using System.Collections.Concurrent;
using System.Diagnostics.Tracing;

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

        if (game.HasPlayer(player))
        {
            RejoinGame(gameId, player);
            return;
        }

        if (!game.TryAssignEmptySeat(player))
            throw new InvalidOperationException("Game is full");

        game.TryStartGame();
    }

    public void RejoinGame(Guid gameId, Player player)
    {
        var seat = GetGame(gameId).GetTablePlayer(player.Guid)
            ?? throw new InvalidOperationException("Player not part of game");
        seat.Player.ConnectionId = player.ConnectionId;
        seat.IsConnected = true;
    }

    public void DisconnectPlayer(Guid gameId, Guid playerId)
    {
        var seat = GetGame(gameId).GetTablePlayer(playerId);
        seat?.IsConnected = false;
    }

    public void LeaveGame(Guid gameId, Guid playerId)
    {
        var seat = GetGame(gameId).GetTablePlayer(playerId);
        seat?.IsConnected = false;
        seat?.Player.Name = string.Empty;
        seat?.Player.Guid = Guid.Empty;
        seat?.Player.ConnectionId = null;
    }

    public void SubmitBidding1(Guid gameId, Guid playerId, BiddingState request)
    {
        var game = GetGame(gameId);
        var seat = game.GetTablePlayer(playerId) ?? throw new InvalidOperationException("Player not in game");

        game.SetBidding1(seat.Position, request);
    }

    public void SubmitBidding2(Guid gameId, Guid playerId, BiddingState request)
    {
        var game = GetGame(gameId);
        var seat = game.GetTablePlayer(playerId)
            ?? throw new InvalidOperationException("Player not in game");

        game.SetBidding2(seat.Position, request);
    }

    public void PlayCard(Guid gameId, Guid playerId, Card card)
    {
        var game = GetGame(gameId);
        var seat = game.GetTablePlayer(playerId)
            ?? throw new InvalidOperationException("Player not in game");

        game.PlayCard(seat.Position, card);
    }


    public async Task BroadcastGame(Guid gameId)
    {
        var game = GetGame(gameId);

        List<string> connectionIds = game.Table.Players
            .Where(p => p.IsConnected && p.Player.Guid != Guid.Empty && !string.IsNullOrEmpty(p.Player.ConnectionId))
            .Select(p => p.Player.ConnectionId ?? string.Empty) // see note below
            .ToList();

        foreach (var seat in game.Table.Players)
        {
            if (!seat.IsConnected || string.IsNullOrEmpty(seat.Player.ConnectionId)) continue;

            await _hub.Clients
                .Client(seat.Player.ConnectionId)
                .SendAsync("ReceiveGameState", game.ToPublicGameState(seat.Position));
        }

        await _hub.Clients
            .AllExcept(connectionIds)
            .SendAsync("ReceiveGameState", game.ToPublicGameState(-1));
    }

    private Game GetGame(Guid id) =>
        _games.TryGetValue(id, out var g)
            ? g
            : throw new InvalidOperationException("Game not found");

    public void AttachConnection(Guid gameId, Guid playerId, string connectionId)
    {
        var seat = GetGame(gameId).GetTablePlayer(playerId);
        if (seat != null)
        {
            seat.IsConnected = true;
            seat.Player.ConnectionId = connectionId;
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
