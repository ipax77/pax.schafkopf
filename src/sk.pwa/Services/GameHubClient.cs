using Microsoft.AspNetCore.SignalR.Client;
using sk.shared;
using sk.shared.Interfaces;

namespace sk.pwa.Services;

public class GameHubClient(HubConnection _hubConnection) : IAsyncDisposable, IGameHubClient
{
    public PublicGameState? GameState { get; private set; }
    public event Action? OnStateChanged;
    public event Action? OnGameInitialized;

    public async Task StartAsync(Player player, Guid gameId)
    {
        if (_hubConnection.State != HubConnectionState.Disconnected)
            return;

        GameState = null;
        _hubConnection.On<PublicGameState>("ReceiveGameState", state =>
        {
            var isInitial = GameState is null;
            GameState = state;

            if (isInitial)
            {
                OnGameInitialized?.Invoke();
            }
            OnStateChanged?.Invoke();
        });

        // Robust Reconnect: Always tell the server who we are when we come back online
        _hubConnection.Reconnected += async (connectionId) =>
        {
            await _hubConnection.InvokeAsync("RejoinGame", gameId, player);
        };

        await _hubConnection.StartAsync();

        // Initial Entry
        if (gameId == Guid.Empty)
            await _hubConnection.InvokeAsync("CreateNewGame", player);
        else
            await _hubConnection.InvokeAsync("JoinGame", gameId, player);
    }

    public async Task JoinByCode(Player player, string code)
    {
        if (_hubConnection.State != HubConnectionState.Disconnected)
            return;
        GameState = null;

        _hubConnection.On<PublicGameState>("ReceiveGameState", state =>
        {
            var isInitial = GameState is null;
            GameState = state;

            if (isInitial)
            {
                OnGameInitialized?.Invoke();
            }
            OnStateChanged?.Invoke();
        });

        // Robust Reconnect: Always tell the server who we are when we come back online
        _hubConnection.Reconnected += async (connectionId) =>
        {
            await _hubConnection.InvokeAsync("RejoinGame", GameState?.Table.Guid ?? Guid.Empty, player);
        };

        await _hubConnection.StartAsync();

        // Initial Entry
        await _hubConnection.InvokeAsync("JoinByCode", code, player);
    }

    public async Task Ready()
    {
        if (_hubConnection is null) return;
        await _hubConnection.SendAsync("Ready");
    }

    public async Task SubmitBidding1(bool wouldPlay)
    {
        if (_hubConnection is null) return;

        await _hubConnection.SendAsync(
            "SubmitBidding1",
            new BiddingState
            {
                WouldPlay = wouldPlay
            });
    }

    public async Task SubmitBidding2(
        Bidding2Result? result)
    {
        if (_hubConnection is null) return;

        BiddingState biddingState = result == null ? new() { WouldPlay = false }
            : new()
            {
                WouldPlay = true,
                ProposedGame = result.GameType,
                ProposedSuit = result.Suit,
                Tout = result.Tout
            };

        await _hubConnection.SendAsync("SubmitBidding2", biddingState);
    }

    public async Task PlayCard(Card card)
    {
        if (_hubConnection is null) return;

        await _hubConnection.SendAsync("PlayCard", card);
    }

    public async Task LeaveTable()
    {
        if (_hubConnection is null) return;
        await _hubConnection.SendAsync("LeaveGame");
    }

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection is not null)
        {
            await _hubConnection.DisposeAsync();
        }
    }
}