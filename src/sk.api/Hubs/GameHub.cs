using Microsoft.AspNetCore.SignalR;
using sk.api.Services;
using sk.shared;

namespace sk.api.Hubs;

public class GameHub(GameHubService service) : Hub
{
    public async Task<Guid> CreateNewGame(Player player)
    {
        player.ConnectionId = Context.ConnectionId;
        Context.Items["playerId"] = player.Guid;
        var gameId = service.CreateNewGame(player);
        Context.Items["gameId"] = gameId;
        await service.BroadcastGame(gameId);
        return gameId;
    }

    public async Task JoinByCode(string shortCode, Player player)
    {
        player.ConnectionId = Context.ConnectionId;
        Context.Items["playerId"] = player.Guid;
        var gameId = service.JoinGame(shortCode, player);
        Context.Items["gameId"] = gameId;
        await service.BroadcastGame(gameId);
    }

    public async Task JoinGame(Guid gameId, Player player)
    {
        player.ConnectionId = Context.ConnectionId;
        Context.Items["playerId"] = player.Guid;
        Context.Items["gameId"] = gameId;
        service.JoinGame(gameId, player);
        await service.BroadcastGame(gameId);
    }

    public async Task RejoinGame(Guid gameId, Player player)
    {
        player.ConnectionId = Context.ConnectionId;
        Context.Items["playerId"] = player.Guid;
        Context.Items["gameId"] = gameId;
        service.RejoinGame(gameId, player);
        await service.BroadcastGame(gameId);
    }

    public async Task Ready()
    {
        if (Context.Items.TryGetValue("gameId", out var gameIdObj)
            && Context.Items.TryGetValue("playerId", out var playerIdObj)
            && gameIdObj is Guid gameId && playerIdObj is Guid playerId)
        {
            service.Ready(gameId, playerId);
            await service.BroadcastGame(gameId);
        }
    }

    public async Task SubmitBidding1(BiddingState request)
    {
        if (Context.Items.TryGetValue("gameId", out var gameIdObj)
            && Context.Items.TryGetValue("playerId", out var playerIdObj)
            && gameIdObj is Guid gameId && playerIdObj is Guid playerId)
        {
            service.SubmitBidding1(gameId, playerId, request);
            await service.BroadcastGame(gameId);
        }
    }

    public async Task SubmitBidding2(BiddingState request)
    {
        if (Context.Items.TryGetValue("gameId", out var gameIdObj)
            && Context.Items.TryGetValue("playerId", out var playerIdObj)
            && gameIdObj is Guid gameId && playerIdObj is Guid playerId)
        {
            service.SubmitBidding2(gameId, playerId, request);
            await service.BroadcastGame(gameId);
        }
    }

    public async Task PlayCard(Card card)
    {
        if (Context.Items.TryGetValue("gameId", out var gameIdObj)
            && Context.Items.TryGetValue("playerId", out var playerIdObj)
            && gameIdObj is Guid gameId && playerIdObj is Guid playerId)
        {
            service.PlayCard(gameId, playerId, card);
            await service.BroadcastGame(gameId);
        }
    }

    public async Task LeaveGame()
    {
        if (Context.Items.TryGetValue("playerId", out var p) &&
            Context.Items.TryGetValue("gameId", out var g)
            && p is Guid pGuid && g is Guid gGuid)
        {
            service.LeaveGame(gGuid, pGuid);
            await service.BroadcastGame((Guid)g);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (Context.Items.TryGetValue("playerId", out var p) &&
            Context.Items.TryGetValue("gameId", out var g)
            && p is Guid pGuid && g is Guid gGuid)
        {
            service.DisconnectPlayer(gGuid, pGuid);
            await service.BroadcastGame((Guid)g);
        }

        await base.OnDisconnectedAsync(exception);
    }
}

