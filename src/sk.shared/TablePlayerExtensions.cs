
namespace sk.shared;

public static class TableLayerExtensions
{

}

public static class GameExtensions
{
    private static readonly Rank[] Ranks =
        [
            Rank.Seven, Rank.Eight, Rank.Nine,
            Rank.Unter, Rank.Ober,
            Rank.King, Rank.Ten, Rank.Ace
        ];

    private static readonly Suit[] Suits =
        [
        Suit.Schellen, Suit.Herz, Suit.Gras, Suit.Eichel
        ];

    public static void DealCards(this Game game)
    {
        var deck = CreateDeck().Shuffle();
        var hands = deck.Chunk(8).ToArray();

        for (int i = 0; i < 4; i++)
        {
            game.Table.Players[i].Hand = [.. hands[i]];

        }
    }

    private static Card[] CreateDeck()
    {
        var deck = new Card[32];
        int i = 0;

        foreach (var suit in Suits)
            foreach (var rank in Ranks)
                deck[i++] = new Card { Rank = rank, Suit = suit };

        return deck;
    }
}