
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
