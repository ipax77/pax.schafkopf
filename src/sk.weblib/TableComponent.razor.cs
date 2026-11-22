using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using sk.shared;
using sk.shared.Interfaces;
using sk.weblib.Modals;
using sk.weblib.Presentation;

namespace sk.weblib;

public partial class TableComponent : ComponentBase, IAsyncDisposable
{
    [Inject] public IGameHubClient GameHubClient { get; set; } = default!;
    [Inject] public NavigationManager NavigationManager { get; set; } = default!;
    [Inject] public IJSRuntime JSRuntime { get; set; } = default!;
    [Parameter, EditorRequired] public Player Player { get; set; } = default!;
    [Parameter] public EventCallback OnTableLeft { get; set; }
    [Parameter] public string? Version { get; set; }

    private PublicGameState publicGameState = new();
    private readonly Queue<(PublicGameState State, bool Resync)> incoming = new();
    private bool receiving, rendered, disposed, trickFinished = true, linkCopied;
    private string? shownModal;
    private DotNetObjectReference<TableComponent>? reference;
    private TrickComponent? trickComponent;
    private Bidding1Modal? bidding1Modal;
    private Bidding2Modal? bidding2Modal;
    private LastTrickModal? lastTrickModal;
    private GameResultModal? gameResultModal;

    private bool AllPlayersAvailable => publicGameState.Table.Players.All(p =>
        p.Player.Guid != Guid.Empty && (p.IsBot || p.IsConnected));
    private bool CanAct => AllPlayersAvailable && publicGameState.ActivePlayer == publicGameState.YourPosition
        && publicGameState.GameState is GameState.Bidding1 or GameState.Bidding2 or GameState.Playing;
    private string ContractLabel => publicGameState.GameState switch
    {
        GameState.Bidding1 => "Wer spielt?",
        GameState.Bidding2 => "Das Spiel wird angesagt",
        GameState.Finished => publicGameState.Bidding2Result?.GetGameTypeString() ?? "Alle haben weitergesagt",
        _ => publicGameState.Bidding2Result?.GetGameTypeString() ?? "Schön, dass du da bist."
    };
    private string? DeclarerName => publicGameState.Bidding2Result is { } bid
        ? publicGameState.GameState == GameState.Finished
            ? publicGameState.PublicGameResult?.GameResult.Player.Name
            : publicGameState.Table.Players.ElementAtOrDefault(bid.PlayerIndex)?.Player.Name
        : null;
    private bool IsLocalDeclarer => publicGameState.GameState == GameState.Playing
        && publicGameState.Bidding2Result?.PlayerIndex == publicGameState.YourPosition;
    private string TurnLabel => !AllPlayersAvailable ? "Warten auf Mitspieler"
        : publicGameState.GameState == GameState.Finished ? "Runde beendet"
        : CanAct ? "Du bist am Zug" : "Mitspieler am Zug";
    private List<PlayerViewInfo> playersByView => publicGameState.Table.Players
        .Select((p, i) => new PlayerViewInfo(p, i, (i - (publicGameState.YourPosition ?? 0) + 4) % 4))
        .OrderBy(p => p.ViewIndex).ToList();

    protected override void OnInitialized()
    {
        publicGameState = GameHubClient.GameState ?? new();
        GameHubClient.OnStateChanged += HandleStateChanged;
    }

    private void HandleStateChanged(PublicGameState state, bool resync) => _ = InvokeAsync(async () =>
    {
        if (disposed) return;
        incoming.Enqueue((state, resync));
        if (rendered) await ReceiveAsync();
    });

    private async Task ReceiveAsync()
    {
        if (receiving || disposed) return;
        receiving = true;
        try
        {
            while (incoming.TryDequeue(out var update) && !disposed)
            {
                if (!update.Resync && update.State.Table.Guid == publicGameState.Table.Guid
                    && update.State.Revision < publicGameState.Revision) continue;
                // Measure the old DOM before exposing the new hand to Blazor.
                var origins = await JSRuntime.InvokeAsync<Dictionary<string, CardOrigin>>("skTable.captureSources");
                publicGameState = update.State;
                gameResultModal?.Refresh(publicGameState);
                if (update.Resync) shownModal = null;
                if (trickComponent is not null)
                    await trickComponent.UpdateAsync(publicGameState, origins, update.Resync);
                ShowModals();
                StateHasChanged();
            }
        }
        finally { receiving = false; }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        reference = DotNetObjectReference.Create(this);
        await JSRuntime.InvokeVoidAsync("skTable.init", reference);
        rendered = true;
        if (trickComponent is not null) await trickComponent.UpdateAsync(publicGameState, [], true);
        ShowModals();
        await ReceiveAsync();
    }

    [JSInvokable]
    public async Task ViewportChanged()
    {
        if (disposed || trickComponent is null) return;
        await trickComponent.UpdateAsync(publicGameState, [], true);
    }

    private void ShowModals()
    {
        if (!rendered || disposed) return;
        string? next = publicGameState.GameState switch
        {
            GameState.Finished when trickFinished && !publicGameState.Table.Players.Any(p =>
                p.Player.Guid == Player.Guid && p.ReadyForNextRound) => $"result-{publicGameState.RoundNumber}",
            GameState.Bidding1 when CanAct => $"bid1-{publicGameState.RoundNumber}",
            GameState.Bidding2 when CanAct => $"bid2-{publicGameState.RoundNumber}",
            _ => null
        };
        if (next == shownModal) return;
        shownModal = next;
        if (next?.StartsWith("result-") == true) gameResultModal?.Show(publicGameState, playersByView);
        else if (next?.StartsWith("bid1-") == true) bidding1Modal?.Show(publicGameState.Bidding1Result?.InterestedPlayers.Count > 0);
        else if (next?.StartsWith("bid2-") == true) bidding2Modal?.Show(publicGameState);
        else
        {
            bidding1Modal?.Close();
            bidding2Modal?.Close();
        }
    }

    private void TrickFinished(bool finished)
    {
        trickFinished = finished;
        if (finished) ShowModals();
    }

    private async Task CopyLink()
    {
        await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", $"{NavigationManager.BaseUri}?TableId={publicGameState.ShortCode}");
        linkCopied = true;
    }
    private Task SubmitBidding1(bool wouldPlay) => GameHubClient.SubmitBidding1(wouldPlay);
    private Task SubmitBidding2(Bidding2Result? result) => GameHubClient.SubmitBidding2(result);
    private Task PlayCard(Card card) => GameHubClient.PlayCard(card);
    private async Task LeaveTable()
    {
        await JSRuntime.InvokeVoidAsync("skTable.closeModals");
        await GameHubClient.LeaveTable();
        await OnTableLeft.InvokeAsync();
    }
    public async ValueTask DisposeAsync()
    {
        disposed = true;
        GameHubClient.OnStateChanged -= HandleStateChanged;
        incoming.Clear();
        try { await JSRuntime.InvokeVoidAsync("skTable.dispose"); }
        catch (JSDisconnectedException) { }
        reference?.Dispose();
    }
}
