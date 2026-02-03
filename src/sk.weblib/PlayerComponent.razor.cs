using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using sk.shared;

namespace sk.weblib;

public partial class PlayerComponent
{
    [Parameter, EditorRequired]
    public TablePlayer TablePlayer { get; set; } = default!;

    [Parameter, EditorRequired]
    public PublicGameState PublicGameState { get; set; } = default!;


    [Parameter]
    public EventCallback<Card> OnCardPlayed { get; set; }

    GameType gameType => PublicGameState.Bidding2Result?.GameType ?? GameType.Ruf;
    Suit suit => PublicGameState.Bidding2Result?.Suit ?? Suit.Herz;

    private Card? draggedCard;
    private double dragStartY;
    private const double DragThreshold = 50;

    protected override void OnParametersSet()
    {
        TablePlayer.Hand = [
            new Card() { Rank = Rank.Ace, Suit = Suit.Eichel },
            new Card() { Rank = Rank.Eight, Suit = Suit.Eichel },
            new Card() { Rank = Rank.Unter, Suit = Suit.Eichel },
            new Card() { Rank = Rank.Ober, Suit = Suit.Eichel },
            new Card() { Rank = Rank.King, Suit = Suit.Eichel },
            new Card() { Rank = Rank.Seven, Suit = Suit.Eichel },
            new Card() { Rank = Rank.Nine, Suit = Suit.Eichel },
            new Card() { Rank = Rank.Ten, Suit = Suit.Eichel },
        ];
        base.OnParametersSet();
    }

    private void PlayCard(Card card)
    {
        if (!IsValidCard(card))
        {
            return;
        }
        OnCardPlayed.InvokeAsync(card);
    }

    protected override void OnInitialized()
    {
        TablePlayer.Hand = [
            new Card() { Rank = Rank.Ace, Suit = Suit.Eichel },
            new Card() { Rank = Rank.Eight, Suit = Suit.Eichel },
            new Card() { Rank = Rank.Unter, Suit = Suit.Eichel },
            new Card() { Rank = Rank.Ober, Suit = Suit.Eichel },
            new Card() { Rank = Rank.King, Suit = Suit.Eichel },
            new Card() { Rank = Rank.Seven, Suit = Suit.Eichel },
            new Card() { Rank = Rank.Nine, Suit = Suit.Eichel },
            new Card() { Rank = Rank.Ten, Suit = Suit.Eichel },
        ];
        base.OnInitialized();
    }

    private List<Card> GetOrderedCards()
    {
        return TablePlayer.Hand.OrderByDescending(o => o.GetCardOrder(gameType, suit)).ToList();
    }

    private bool IsValidCard(Card card)
    {
        if (PublicGameState.Bidding2Result is null || PublicGameState.ActivePlayer != PublicGameState.YourPosition)
        {
            return false;
        }
        var firstPlayer = (PublicGameState.ActivePlayer + PublicGameState.Table.CurrentTrick.Count(c => c != null)) % 4;
        var firstCard = PublicGameState.Table.CurrentTrick[firstPlayer];
        if (firstCard is null)
        {
            return false;
        }
        return card.CanOperate(firstCard, PublicGameState.Bidding2Result.GameType, PublicGameState.Bidding2Result.Suit);
    }

    private void OnDragStart(MouseEventArgs e, Card card)
    {
        draggedCard = card;
        dragStartY = e.ClientY;
    }

    private void OnDragStart(TouchEventArgs e, Card card)
    {
        if (e.Touches.Length > 0)
        {
            draggedCard = card;
            dragStartY = e.Touches[0].ClientY;
        }
    }

    private void OnDragMove(MouseEventArgs e)
    {
        if (draggedCard != null && e.Buttons == 1) // Left button pressed
        {
            var dragDistance = dragStartY - e.ClientY; // Positive when dragging up
            if (dragDistance > DragThreshold)
            {
                PlayCard(draggedCard);
                draggedCard = null;
            }
        }
    }

    private void OnDragMove(TouchEventArgs e)
    {
        if (draggedCard != null && e.Touches.Length > 0)
        {
            var dragDistance = dragStartY - e.Touches[0].ClientY;
            if (dragDistance > DragThreshold)
            {
                PlayCard(draggedCard);
                draggedCard = null;
            }
        }
    }

    private void OnDragEnd(MouseEventArgs e)
    {
        draggedCard = null;
    }

    private void OnDragEnd(TouchEventArgs e)
    {
        draggedCard = null;
    }
}