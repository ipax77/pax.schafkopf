using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using sk.shared;
using sk.weblib.Modals;

namespace sk.weblib;

public partial class TableComponent(IHttpClientFactory httpClientFactory) : ComponentBase, IAsyncDisposable
{
    [Parameter, EditorRequired]
    public Player Player { get; set; } = default!;

    [Parameter]
    public Guid GameGuid { get; set; }

    [Parameter]
    public EventCallback<Guid> OnGuidSet { get; set; }

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
    private PlayerComponent? playerComponent;
    private TrickComponent? trickComponent;
    private Bidding1Modal? bidding1Modal;
    private Bidding2Modal? bidding2Modal;

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
                if (state.Table.CurrentTrickCard != null)
                    trickComponent?.AddCard(state.Table.CurrentTrickCard);
                InvokeAsync(StateHasChanged);
                if (GameGuid == Guid.Empty)
                {
                    GameGuid = publicGameState.Table.Guid;
                    OnGuidSet.InvokeAsync(GameGuid);
                }
                ShowModals();
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
                Player);
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
                .Select((p, serverIndex) => new PlayerViewInfo(p, serverIndex, serverIndex))
                .OrderBy(p => p.ViewIndex)
                .ToList();
        }
    }

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
        {
            ShowModals();
        }
        base.OnAfterRender(firstRender);
    }

    private void ShowModals()
    {
        if (publicGameState.ActivePlayer != publicGameState.YourPosition)
        {
            return;
        }
        if (publicGameState.GameState == GameState.Bidding1)
        {
            bidding1Modal?.Show(publicGameState.Bidding1Result?.InterestedPlayers.Count > 0);
        }
        else if (publicGameState.GameState == GameState.Bidding2)
        {
            bidding2Modal?.Show(publicGameState);
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
        Bidding2Result? result)
    {
        if (hubConnection is null) return;

        BiddingState biddingState = result == null ? new() { WouldPlay = false }
            : new()
            {
                WouldPlay = true,
                ProposedGame = result.GameType,
                ProposedSuit = result.Suit,
                Tout = result.Tout
            };

        await hubConnection.SendAsync("SubmitBidding2", biddingState);
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
}