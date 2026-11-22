using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using sk.shared;

namespace sk.weblib;

public partial class PlayerComponent : IAsyncDisposable
{
    [Inject] public IJSRuntime JSRuntime { get; set; } = default!;
    [CascadingParameter] public PublicGameState PublicGameState { get; set; } = default!;
    [Parameter, EditorRequired] public PlayerViewInfo PlayerViewInfo { get; set; } = default!;
    [Parameter] public EventCallback<Card> OnCardPlayed { get; set; }
    [Parameter] public bool Compact { get; set; }
    private bool Inspecting => Compact && PublicGameState.GameState is GameState.Bidding1 or GameState.Bidding2;

    private ElementReference handElement;
    private DotNetObjectReference<PlayerComponent>? reference;
    private bool pending;
    private string? error;
    private List<Card> validCards => PublicGameState.Table.Players.All(p =>
        p.Player.Guid != Guid.Empty && (p.IsBot || p.IsConnected)) ? PublicGameState.GetValidCards() : [];

    private List<Card> GetOrderedCards() => PlayerViewInfo.TablePlayer.Hand
        .OrderByDescending(c => c.GetCardOrder(PublicGameState.Bidding2Result?.GameType ?? GameType.Ruf,
            PublicGameState.Bidding2Result?.Suit ?? Suit.Herz)).ToList();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        reference ??= DotNetObjectReference.Create(this);
        await JSRuntime.InvokeVoidAsync("skTable.bindHand", handElement, reference);
    }

    [JSInvokable]
    public async Task<bool> PlayFromHand(int cardId)
    {
        if (pending || cardId is < 0 or > 31) return false;
        var card = GameAction.FromCardId(cardId);
        if (!validCards.Contains(card)) return false;
        pending = true;
        error = null;
        await InvokeAsync(StateHasChanged);
        try
        {
            await OnCardPlayed.InvokeAsync(card);
            return true;
        }
        catch
        {
            error = "Die Karte konnte nicht gespielt werden. Bitte versuche es erneut.";
            return false;
        }
        finally
        {
            pending = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { await JSRuntime.InvokeVoidAsync("skTable.unbindHand", handElement); }
        catch (JSDisconnectedException) { }
        reference?.Dispose();
    }
}
