using Microsoft.AspNetCore.SignalR;
using sk.api.Hubs;
using sk.shared;
using System.Collections.Concurrent;

namespace sk.api.Services;

public class GameHubService(IHubContext<GameHub> hub)
{
    private readonly ConcurrentDictionary<Guid, LiveTable> _tables = [];
    private readonly ConcurrentDictionary<string, Guid> _joinCodeMap = new();

    private sealed class LiveTable
    {
        public Game Game { get; } = new();
        public SemaphoreSlim BroadcastLock { get; } = new(1, 1);
        public DateTimeOffset? DisconnectedSince { get; set; }
        public bool Removed { get; set; }
    }

    public Guid[] GameIds => _tables.Keys.ToArray();

    public Guid CreateNewGame(Player player)
    {
        if (player.Guid == Guid.Empty) throw new InvalidOperationException("Player identity required");
        var table = new LiveTable();
        var game = table.Game;
        game.Table.Guid = Guid.NewGuid();
        game.TryAssignEmptySeat(CopyHuman(player));
        do { game.ShortCode = GenerateShortCode(); }
        while (!_joinCodeMap.TryAdd(game.ShortCode, game.Table.Guid));
        _tables[game.Table.Guid] = table;
        return game.Table.Guid;
    }

    public Guid JoinGame(string gameCode, Player player)
    {
        if (!_joinCodeMap.TryGetValue(gameCode.Trim().ToUpperInvariant(), out var guid))
            throw new InvalidOperationException("Game not found");
        JoinGame(guid, player);
        return guid;
    }

    public void JoinGame(Guid gameId, Player player)
    {
        if (player.Guid == Guid.Empty) throw new InvalidOperationException("Player identity required");
        var table = GetTable(gameId);
        var game = table.Game;
        lock (game.joinLock)
        {
            EnsurePresent(table);
            if (game.HasPlayer(player))
            {
                RejoinGame(gameId, player);
                return;
            }
            EnsureBetweenRounds(game);
            if (!game.TryAssignEmptySeat(CopyHuman(player)))
                throw new InvalidOperationException("Game is full. Remove a bot between rounds to free a seat.");
            MembershipChanged(table);
            game.TryStartGame();
        }
    }

    public void RejoinGame(Guid gameId, Player player) => AttachConnection(gameId, player.Guid, player.ConnectionId);

    public void AttachConnection(Guid gameId, Guid playerId, string? connectionId)
    {
        var table = GetTable(gameId);
        var game = table.Game;
        lock (game.joinLock)
        {
            EnsurePresent(table);
            var seat = HumanSeat(game, playerId);
            seat.IsConnected = true;
            seat.Player.ConnectionId = connectionId;
            table.DisconnectedSince = null;
            game.InvalidatePendingActions();
            game.TryStartGame();
            TryStartNextRound(game);
        }
    }

    public void DisconnectPlayer(Guid gameId, Guid playerId, string? connectionId = null)
    {
        if (!_tables.TryGetValue(gameId, out var table)) return;
        var game = table.Game;
        lock (game.joinLock)
        {
            var seat = game.GetTablePlayer(playerId);
            // Ignore a late disconnect from the connection replaced during rejoin.
            if (seat == null || seat.IsBot || (connectionId != null && seat.Player.ConnectionId != connectionId)) return;
            seat.IsConnected = false;
            seat.Player.ConnectionId = null;
            game.InvalidatePendingActions();
            if (!HasConnectedHuman(game)) table.DisconnectedSince ??= DateTimeOffset.UtcNow;
        }
    }

    public void LeaveGame(Guid gameId, Guid playerId)
    {
        if (!_tables.TryGetValue(gameId, out var table)) return;
        var game = table.Game;
        lock (game.joinLock)
        {
            var seat = HumanSeat(game, playerId);
            seat.IsConnected = false;
            seat.Player.ConnectionId = null;
            // Reserve the identity/hand during a deal so that the player can rejoin.
            if (game.GameState is GameState.None or GameState.Finished) ClearSeat(seat);
            MembershipChanged(table);
            if (!HasConnectedHuman(game)) RemoveTable(table);
        }
    }

    public void FillBotSeats(Guid gameId, Guid playerId)
    {
        var table = GetTable(gameId);
        var game = table.Game;
        lock (game.joinLock)
        {
            EnsurePresent(table);
            HumanSeat(game, playerId, connected: true);
            EnsureBetweenRounds(game);
            string[] names = ["Bot Alois", "Bot Berta", "Bot Sepp", "Bot Resi"];
            foreach (var seat in game.Table.Players.Where(p => p.Player.Guid == Guid.Empty))
            {
                seat.Player = new Player { Guid = Guid.NewGuid(), Name = names[seat.Position] };
                seat.IsBot = true;
                seat.IsConnected = false;
            }
            MembershipChanged(table);
            game.TryStartGame();
        }
    }

    public void RemoveBot(Guid gameId, Guid playerId, int position)
    {
        var table = GetTable(gameId);
        var game = table.Game;
        lock (game.joinLock)
        {
            EnsurePresent(table);
            HumanSeat(game, playerId, connected: true);
            EnsureBetweenRounds(game);
            if (position is < 0 or > 3 || !game.Table.Players[position].IsBot)
                throw new InvalidOperationException("Seat is not a bot");
            ClearSeat(game.Table.Players[position]);
            MembershipChanged(table);
        }
    }

    public bool Ready(Guid gameId, Guid playerId)
    {
        var table = GetTable(gameId);
        var game = table.Game;
        lock (game.joinLock)
        {
            EnsurePresent(table);
            var seat = HumanSeat(game, playerId, connected: true);
            if (game.GameState != GameState.Finished) return false;
            seat.ReadyForNextRound = true;
            return TryStartNextRound(game);
        }
    }

    public void SubmitBidding1(Guid gameId, Guid playerId, BiddingState request) =>
        Act(gameId, playerId, (g, seat) => g.SetBidding1(seat.Position, request));
    public void SubmitBidding2(Guid gameId, Guid playerId, BiddingState request) =>
        Act(gameId, playerId, (g, seat) => g.SetBidding2(seat.Position, request));
    public void PlayCard(Guid gameId, Guid playerId, Card card) =>
        Act(gameId, playerId, (g, seat) => g.PlayCard(seat.Position, card));

    private void Act(Guid gameId, Guid playerId, Action<Game, TablePlayer> action)
    {
        var table = GetTable(gameId);
        var game = table.Game;
        lock (game.joinLock)
        {
            EnsurePresent(table);
            var seat = HumanSeat(game, playerId, connected: true);
            if (!CanPlay(game)) throw new InvalidOperationException("Waiting for players to reconnect");
            action(game, seat);
            ReadyBots(game);
        }
    }

    public async Task BroadcastGame(Guid gameId, CancellationToken cancellationToken = default)
    {
        if (!_tables.TryGetValue(gameId, out var table)) return;
        // Serialize snapshot creation and delivery so an old state cannot overtake a new one.
        await table.BroadcastLock.WaitAsync(cancellationToken);
        try
        {
            List<(string Connection, PublicGameState State)> snapshots;
            var game = table.Game;
            lock (game.joinLock)
            {
                if (table.Removed) return;
                snapshots = game.Table.Players
                    .Where(p => !p.IsBot && p.IsConnected && !string.IsNullOrEmpty(p.Player.ConnectionId))
                    .Select(p => (p.Player.ConnectionId!, game.ToPublicGameState(p.Position))).ToList();
            }
            await Task.WhenAll(snapshots.Select(s => hub.Clients.Client(s.Connection)
                .SendAsync("ReceiveGameState", s.State, cancellationToken)));
        }
        finally { table.BroadcastLock.Release(); }
    }

    // Detached, seat-limited observations, also usable by human policy test adapters.
    public BotTurn GetBotTurn(Guid gameId, Guid playerId)
    {
        var table = GetTable(gameId);
        var game = table.Game;
        lock (game.joinLock)
        {
            EnsurePresent(table);
            var seat = game.GetTablePlayer(playerId) ?? throw new InvalidOperationException("Player not in game");
            return new(game.Revision, game.Observe(seat.Position));
        }
    }

    public ScheduledBotTurn? GetActiveBotTurn(Guid gameId)
    {
        if (!_tables.TryGetValue(gameId, out var table)) return null;
        var game = table.Game;
        lock (game.joinLock)
        {
            if (table.Removed || !CanPlay(game) || game.GameState is GameState.None or GameState.Finished) return null;
            var seat = game.Table.Players[game.ActivePlayer];
            return seat.IsBot ? new(gameId, seat.Player.Guid, new(game.Revision, game.Observe(seat.Position))) : null;
        }
    }

    public bool TryApplyBotAction(Guid gameId, Guid playerId, long revision, GameAction action)
    {
        if (!_tables.TryGetValue(gameId, out var table)) return false;
        var game = table.Game;
        lock (game.joinLock)
        {
            var seat = game.GetTablePlayer(playerId);
            if (table.Removed || seat is not { IsBot: true } || !CanPlay(game) ||
                game.Revision != revision || game.ActivePlayer != seat.Position || !game.GetLegalActions().Contains(action)) return false;
            game.Step(seat.Position, action);
            ReadyBots(game);
            return true;
        }
    }

    public void CleanupAbandonedTables(DateTimeOffset now, TimeSpan retention)
    {
        foreach (var table in _tables.Values)
        {
            lock (table.Game.joinLock)
            {
                if (table.DisconnectedSince is { } since && now - since >= retention && !HasConnectedHuman(table.Game))
                    RemoveTable(table);
            }
        }
    }

    private void RemoveTable(LiveTable table)
    {
        if (table.Removed) return;
        table.Removed = true;
        table.Game.InvalidatePendingActions();
        _tables.TryRemove(table.Game.Table.Guid, out _);
        _joinCodeMap.TryRemove(table.Game.ShortCode, out _);
    }

    private static bool HasConnectedHuman(Game game) => game.Table.Players.Any(p => !p.IsBot && p.IsConnected);
    private static bool CanPlay(Game game) => HasConnectedHuman(game) && game.Table.Players.All(p =>
        p.Player.Guid != Guid.Empty && (p.IsBot || p.IsConnected));
    private static void ReadyBots(Game game)
    {
        if (game.GameState == GameState.Finished)
            foreach (var seat in game.Table.Players.Where(p => p.IsBot)) seat.ReadyForNextRound = true;
    }
    private static bool TryStartNextRound(Game game)
    {
        if (game.GameState != GameState.Finished || !CanPlay(game) || !game.Table.Players.All(p => p.ReadyForNextRound)) return false;
        game.Reset();
        return true;
    }
    private static void MembershipChanged(LiveTable table)
    {
        table.Game.InvalidatePendingActions();
        // Changed lineups require fresh human consent to the next deal.
        foreach (var seat in table.Game.Table.Players) seat.ReadyForNextRound = false;
        ReadyBots(table.Game);
    }
    private static void ClearSeat(TablePlayer seat)
    {
        seat.Player = new();
        seat.IsBot = seat.IsConnected = seat.ReadyForNextRound = false;
        // Preserve the completed deal and the seat's ledger until the next round.
    }
    private static void EnsureBetweenRounds(Game game)
    {
        if (game.GameState is not (GameState.None or GameState.Finished))
            throw new InvalidOperationException("Seats can only be changed between rounds");
    }
    private static TablePlayer HumanSeat(Game game, Guid playerId, bool connected = false)
    {
        var seat = playerId == Guid.Empty ? null : game.GetTablePlayer(playerId);
        if (seat == null || seat.IsBot || (connected && !seat.IsConnected))
            throw new InvalidOperationException("Connected human player required");
        return seat;
    }
    private static Player CopyHuman(Player player) => new() { Guid = player.Guid, Name = player.Name, ConnectionId = player.ConnectionId };
    private static void EnsurePresent(LiveTable table)
    {
        if (table.Removed) throw new InvalidOperationException("Game not found");
    }
    private LiveTable GetTable(Guid id) => _tables.TryGetValue(id, out var table)
        ? table : throw new InvalidOperationException("Game not found");
    private static string GenerateShortCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Range(0, 4).Select(_ => chars[Random.Shared.Next(chars.Length)]).ToArray());
    }
}

public sealed record BotTurn(long Revision, GameObservation Observation);
public sealed record ScheduledBotTurn(Guid GameId, Guid PlayerId, BotTurn Turn);
