using Microsoft.AspNetCore.Components;
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
        return TablePlayer.Hand.OrderBy(o => o.GetCardOrder(gameType, suit)).ToList();
    }
}