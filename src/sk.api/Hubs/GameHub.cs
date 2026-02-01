using Microsoft.AspNetCore.SignalR;
using sk.api.Services;
using sk.shared;

namespace sk.api.Hubs;

public class GameHub(GameHubService service) : Hub
{
    public async Task<Guid> CreateNewGame(Player player)
    {
        Context.Items["playerId"] = player.Guid;

        var gameId = service.CreateNewGame(player);
        Context.Items["gameId"] = gameId;

        await service.BroadcastGame(gameId);
        return gameId;
    }

    public async Task JoinGame(Guid gameId, Player player)
    {
        Context.Items["playerId"] = player.Guid;
        Context.Items["gameId"]   = gameId;
        player.ConnectionId = Context.ConnectionId;
        service.JoinGame(gameId, player);
        await service.BroadcastGame(gameId);
    }

    public async Task RejoinGame(Guid gameId, Guid playerId)
    {
        Context.Items["playerId"] = playerId;
        Context.Items["gameId"]   = gameId;
        service.RejoinGame(gameId, playerId);
        await service.BroadcastGame(gameId);
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

