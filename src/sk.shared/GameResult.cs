namespace sk.shared;

public sealed record GameResult
{
    public GameType GameType { get; init; }
    public Suit Suit { get; init; }
    public bool Tout { get; init; }
    public Player Player { get; init; } = new();
    public Player? Player2 { get; init; }
    public int Runners { get; init; }
    public bool WithoutRunners { get; init; }
    public int PlayerPoints { get; init; }
    public int PlayerTricks { get; init; }
    public int Cost { get; set; }
    public bool DeclarerWins => Tout || GameType == GameType.Sie ? PlayerTricks == 8 : PlayerPoints > 60;
    public bool Schneider => PlayerPoints <= 30 || PlayerPoints > 90;
    public bool Schwarz => PlayerTricks is 0 or 8;

    public void SetGameCost()
    {
        if (GameType == GameType.None) { Cost = 0; return; }
        int cost = GameType == GameType.Ruf ? 10 : 20;
        if (Runners >= (GameType == GameType.Wenz ? 2 : 3)) cost += Runners * 10;
        if (GameType == GameType.Sie) cost *= 4;
        else if (Tout) cost *= 2;
        else
        {
            if (Schneider) cost += 10;
            if (Schwarz) cost += 10;
        }
        Cost = cost;
    }
}

public static class GameScoring
{
    // Private immutable card sequences: Wenz at index 0, suit Solos at 1..4.
    private static readonly Card[][] TrumpOrders = Enumerable.Range(0, 5).Select(s =>
        Enumerable.Range(0, 32).Select(GameAction.FromCardId)
            .Where(c => c.IsTrump(s == 0 ? GameType.Wenz : GameType.Solo, (Suit)s))
            .OrderByDescending(c => c.GetCardRank(s == 0 ? GameType.Wenz : GameType.Solo, (Suit)s)).ToArray()).ToArray();

    public static (int Count, bool Without) CountRunners(IReadOnlyCollection<Card> declarerCards, GameType type, Suit suit)
    {
        var trumps = type switch
        {
            GameType.Wenz => TrumpOrders[0],
            GameType.Ruf or GameType.Sie => TrumpOrders[(int)Suit.Herz],
            GameType.Solo when suit is >= Suit.Schellen and <= Suit.Eichel => TrumpOrders[(int)suit],
            _ => Enumerable.Range(0, 32).Select(GameAction.FromCardId).Where(c => c.IsTrump(type, suit))
                .OrderByDescending(c => c.GetCardRank(type, suit)).ToArray()
        };
        bool ownsTop = declarerCards.Contains(trumps[0]);
        int count = 0;
        while (count < trumps.Length && declarerCards.Contains(trumps[count]) == ownsTop) count++;
        return (count, !ownsTop);
    }
}
