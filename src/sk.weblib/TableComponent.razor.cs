using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using sk.shared;
using sk.shared.Interfaces;
using sk.weblib.Modals;

namespace sk.weblib;

public partial class TableComponent() : ComponentBase, IDisposable
{
    [Inject]
    public IGameHubClient GameHubClient { get; set; } = default!;

    [Inject]
    public NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    public IJSRuntime JSRuntime { get; set; } = default!;

    [Parameter, EditorRequired]
    public Player Player { get; set; } = default!;

    [Parameter]
    public EventCallback OnTableLeft { get; set; }

    private PublicGameState publicGameState => GameHubClient.GameState ?? new();
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

    private bool trickFinished = true;

    private List<PlayerViewInfo> playersByView => GetPlayersByView();
    private PlayerComponent? playerComponent;
    private TrickComponent? trickComponent;
    private Bidding1Modal? bidding1Modal;
    private Bidding2Modal? bidding2Modal;
    private LastTrickModal? lastTrickModal;
    private GameResultModal? gameResultModal;

    protected override void OnInitialized()
    {
        GameHubClient.OnStateChanged += HandleStateChanged;
        base.OnInitialized();
    }

    private void HandleStateChanged()
    {
        InvokeAsync(() =>
        {
            // Logic for tricks, modals, etc.

            if (GameHubClient.GameState?.Table.CurrentTrickCard != null)
                trickComponent?.AddCard(GameHubClient.GameState.Table.CurrentTrickCard, playersByView);
            ShowModals();

            StateHasChanged();
        });
    }

    async Task CopyLink()
    {
        await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", NavigationManager.Uri);
        // Optional: Trigger a "Copied!" toast
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

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await JSRuntime.InvokeVoidAsync("screenSleep");
            HandleInitialSync();
        }
        await base.OnAfterRenderAsync(firstRender);
    }

    private void HandleInitialSync()
    {
        InvokeAsync(() =>
        {
            if (GameHubClient.GameState != null)// Force the TrickComponent to match the server exactly
                trickComponent?.Sync(GameHubClient.GameState, GetPlayersByView());

            // If we joined a finished game, ensure the modal can show
            if (GameHubClient.GameState?.GameState == GameState.Finished)
            {
                trickFinished = true;
            }

            ShowModals();
            StateHasChanged();
        });
    }

    private void ShowModals()
    {
        if (publicGameState.GameState == GameState.Finished)
        {
            if (trickFinished)
            {
                gameResultModal?.Show(publicGameState, GetPlayersByView());
                return;
            }
        }

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

    private void TrickFinished(bool finished)
    {
        trickFinished = finished;
        if (trickFinished && publicGameState.GameState == GameState.Finished)
        {
            gameResultModal?.Show(publicGameState, GetPlayersByView());
        }
    }

    private async Task SubmitBidding1(bool wouldPlay)
    {
        await GameHubClient.SubmitBidding1(wouldPlay);
    }

    private async Task SubmitBidding2(
        Bidding2Result? result)
    {
        await GameHubClient.SubmitBidding2(result);
    }

    private async Task PlayCard(Card card)
    {
        await GameHubClient.PlayCard(card);
    }

    private void PlayTestCard(int index)
    {
        trickComponent?.AddCard(new() { Card = new() { Rank = (Rank)(index + 1), Suit = Suit.Eichel }, Position = index }, playersByView);
    }

    private async Task LeaveTable()
    {
        await GameHubClient.LeaveTable();
        await OnTableLeft.InvokeAsync();
    }

    public void Dispose()
    {
        GameHubClient.OnStateChanged -= HandleStateChanged;
    }
}