
namespace sk.shared;

public sealed class BiddingStateComparer : IComparer<BiddingState>
{
    public int Compare(BiddingState? a, BiddingState? b)
    {
        int ra = Rank(a);
        int rb = Rank(b);

        return ra.CompareTo(rb);
    }

    private static int Rank(BiddingState? x) => x switch
    {
        null => 0,

        { Sie: true } => 10,
        { Tout: true } => 4,
        { ProposedGame: GameType.Solo } => 3,
        { ProposedGame: GameType.Wenz } => 2,
        { ProposedGame: GameType.Ruf } => 1,

        _ => 0
    };
}

public sealed class TrickCardComparer(GameType gameType, Suit trump, Card firstCard) : IComparer<Card>
{
    public int Compare(Card? a, Card? b)
    {
        bool aTrump = a?.IsTrump(gameType, trump) ?? false;
        bool bTrump = b?.IsTrump(gameType, trump) ?? false;

        if (aTrump != bTrump)
            return aTrump ? 1 : -1;

        if (!aTrump && a?.Suit != b?.Suit)
            return a?.Suit == firstCard.Suit ? 1 : -1;

        return (a?.GetCardRank(gameType, trump, firstCard) ?? 0)
            .CompareTo(b?.GetCardRank(gameType, trump, firstCard) ?? 0);
    }
}



public static class TablePlayerExtensions
{
    public static List<Card> GetValidCards(this TablePlayer player, Game game)
    {
        ArgumentNullException.ThrowIfNull(game.Bidding2Result);
        var playedCards = game.Table.CurrentTrick.Count(c => c != null);
        if (playedCards == 0)
        {
            return player.Hand;
        }

        var firstPlayer = (game.ActivePlayer - playedCards + 4) % 4;
        var firstCard = game.Table.CurrentTrick[firstPlayer];
        ArgumentNullException.ThrowIfNull(firstCard);
        var mustFollow = player.Hand
            .Where(c => c.CanOperate(firstCard,
                game.Bidding2Result.GameType,
                game.Bidding2Result.Suit))
            .ToList();

        // if player cannot follow suit / trump → free choice
        return mustFollow.Count > 0
            ? mustFollow
            : player.Hand;
    }

    public static void Reset(this TablePlayer player)
    {
        player.Hand.Clear();
        player.Tricks.Clear();
    }
}

public static class TableExtensions
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

    public static void CollectTrick(this Game game)
    {
        ArgumentNullException.ThrowIfNull(game.Bidding2Result);

        var trick = new List<(int Player, Card Card)>();

        int player = game.ActivePlayer;

        for (int i = 0; i < 4; i++)
        {
            var card = game.Table.CurrentTrick[player];
            ArgumentNullException.ThrowIfNull(card);

            trick.Add((player, card));
            player = (player + 1) % 4;
        }

        var firstCard = trick[0].Card;
        var comparer = new TrickCardComparer(game.Bidding2Result.GameType, game.Bidding2Result.Suit, firstCard);
        var winner = trick.OrderByDescending(o => o.Card, comparer).First();

        // store trick in play order, not sorted order
        foreach (var (_, card) in trick)
            game.Table.Players[winner.Player].Tricks.Add(card);

        game.Table.PreviouseTrick = game.Table.CurrentTrick;
        game.Table.CurrentTrick = [null, null, null, null];

        game.ActivePlayer = winner.Player;
    }

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