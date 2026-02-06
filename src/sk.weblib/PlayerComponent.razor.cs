using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using sk.shared;

namespace sk.weblib;

public partial class PlayerComponent
{
    [CascadingParameter]
    public PublicGameState PublicGameState { get; set; } = default!;

    [Parameter, EditorRequired]
    public PlayerViewInfo PlayerViewInfo { get; set; } = default!;

    [Parameter]
    public EventCallback<Card> OnCardPlayed { get; set; }

    GameType gameType => PublicGameState.Bidding2Result?.GameType ?? GameType.Ruf;
    Suit suit => PublicGameState.Bidding2Result?.Suit ?? Suit.Herz;
    List<Card> validCards => PublicGameState.GetValidCards();

    private Card? draggedCard;
    private double dragStartY;
    private const double DragThreshold = 50;

    private void PlayCard(Card card)
    {
        if (!IsValidCard(card))
        {
            return;
        }
        OnCardPlayed.InvokeAsync(card);
    }

    private List<Card> GetOrderedCards()
    {
        return PlayerViewInfo.TablePlayer.Hand.OrderByDescending(o => o.GetCardOrder(gameType, suit)).ToList();
    }

    private bool IsValidCard(Card card)
    {
        return validCards.Contains(card);
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