
namespace sk.shared;

public sealed class GameResult
{
    private static readonly int rufCost = 10;
    private static readonly int soloCost = 20;
    private static readonly int runnerCost = 10;
    private static readonly int schneiderCost = 10;
    private static readonly int schwarzCost = 10;

    public GameType GameType { get; init; }
    public Suit Suit { get; init; }
    public bool Tout { get; init; }
    public Player Player { get; init; } = default!;
    public Player? Player2 { get; init; }
    public int Runners { get; init; }
    public int PlayerPoints { get; init; }
    public int Cost { get; set; }

    public void SetGameCost()
    {
        int cost = GameType == GameType.Ruf
            ? rufCost
            : soloCost;

        bool hasRunnerBonus =
            Runners >= 3 ||
            (GameType == GameType.Wenz && Runners >= 2);

        if (hasRunnerBonus)
            cost += Runners * runnerCost;

        if (PlayerPoints is < 30 or > 90)
            cost += schneiderCost;

        if (PlayerPoints is 0 or 120)
            cost += schwarzCost;

        if (Tout)
            cost *= 2;

        if (GameType == GameType.Sie)
            cost *= 2;

        Cost = cost;
    }
}
