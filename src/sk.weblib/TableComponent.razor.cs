using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using sk.shared;

namespace sk.weblib;

public partial class TableComponent(IHttpClientFactory httpClientFactory) : ComponentBase, IAsyncDisposable
{
    [Parameter, EditorRequired]
    public Player Player { get; set; } = default!;

    [Parameter]
    public Guid GameGuid { get; set; }

    private HubConnection? hubConnection;
    private PublicGameState publicGameState = new();
    bool IsMyTurn =>
        publicGameState.YourPosition.HasValue &&
        publicGameState.ActivePlayer == publicGameState.YourPosition.Value;

    bool CanAct =>
        IsMyTurn &&
        publicGameState.GameState is
            GameState.Bidding1 or
            GameState.Bidding2 or
            GameState.Playing;

    int ViewIndex(int serverIndex)
      => (serverIndex - publicGameState.YourPosition!.Value + 4) % 4;

    private List<PlayerViewInfo> playersByView => GetPlayersByView();

    protected override async Task OnInitializedAsync()
    {
        using var httpClient = httpClientFactory.CreateClient("api");

        var baseUri = httpClient.BaseAddress
            ?? new Uri("http://localhost:5283");

        hubConnection = new HubConnectionBuilder()
            .WithUrl(new Uri(baseUri, "/gameHub"))
            .WithAutomaticReconnect()
            .Build();

        hubConnection.On<PublicGameState>(
            "ReceiveGameState",
            state =>
            {
                publicGameState = state;
                InvokeAsync(StateHasChanged);
            });

        hubConnection.Reconnecting += error =>
        {
            // Optional: show "reconnecting..." UI
            return Task.CompletedTask;
        };

        hubConnection.Reconnected += async _ =>
        {
            await hubConnection.SendAsync(
                "RejoinGame",
                GameGuid,
                Player.Guid);
        };

        hubConnection.Closed += async _ =>
        {
            // Optional backoff / logging
            await Task.Delay(2000);
            await hubConnection.StartAsync();
        };

        await hubConnection.StartAsync();

        // Initial join or rejoin
        if (GameGuid == Guid.Empty)
        {
            await hubConnection.SendAsync("CreateNewGame", Player);
        }
        else
        {
            await hubConnection.SendAsync(
                "JoinGame",
                GameGuid,
                Player);
        }
        await base.OnInitializedAsync();
    }

    private List<PlayerViewInfo> GetPlayersByView()
    {
        if (publicGameState.YourPosition.HasValue)
        {
            return publicGameState.Table.Players
                .Select((p, serverIndex) => new PlayerViewInfo(p, serverIndex, (serverIndex - publicGameState.YourPosition.Value + 4) % 4))
                .OrderBy(p => p.ViewIndex)
                .ToList();
        }
        else
        {
            return publicGameState.Table.Players
                .Select((p, serverIndex) => new PlayerViewInfo(p, serverIndex, (serverIndex - 0 + 4) % 4))
                .OrderBy(p => p.ViewIndex)
                .ToList();
        }
    }

    private async Task SubmitBidding1(bool wouldPlay)
    {
        if (hubConnection is null) return;

        await hubConnection.SendAsync(
            "SubmitBidding1",
            new BiddingState
            {
                WouldPlay = wouldPlay
            });
    }

    private async Task SubmitBidding2(
        bool wouldPlay,
        GameType? gameType = null,
        Suit? suit = null,
        bool tout = false)
    {
        if (hubConnection is null) return;

        await hubConnection.SendAsync(
            "SubmitBidding2",
            new BiddingState
            {
                WouldPlay = wouldPlay,
                ProposedGame = gameType,
                ProposedSuit = suit,
                Tout = tout
            });
    }

    private async Task PlayCard(Card card)
    {
        if (hubConnection is null) return;

        await hubConnection.SendAsync("PlayCard", card);
    }

    public async ValueTask DisposeAsync()
    {
        if (hubConnection is not null)
        {
            await hubConnection.DisposeAsync();
        }
    }

    private record PlayerViewInfo(TablePlayer TablePlayer, int ServerIndex, int ViewIndex);
}