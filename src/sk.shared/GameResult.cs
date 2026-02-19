
namespace sk.shared;

public sealed class GameResult
{
    public GameType GameType { get; init; }
    public Suit Suit { get; init; }
    public bool Tout { get; init; }
    public Player Player { get; init; } = default!;
    public Player? Player2 { get; init; }
    public int Runners { get; init; }
    public int PlayerPoints { get; init; }
    public int Cost { get; set; }
}
