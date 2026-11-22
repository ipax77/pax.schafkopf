using sk.shared;

namespace sk.weblib.Presentation;

public static class CardText
{
    public static string Label(Card card) => $"{card.Suit} {card.Rank switch
    {
        Rank.Seven => "Sieben", Rank.Eight => "Acht", Rank.Nine => "Neun",
        Rank.Ten => "Zehn", Rank.King => "König", Rank.Ace => "Ass", _ => card.Rank.ToString()
    }}";
}
