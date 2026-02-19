
namespace sk.shared;

public sealed class Bidding2Result
{
    public int PlayerIndex { get; set; }
    public GameType GameType { get; set; }
    public Suit Suit { get; set; }
    public bool Sie { get; set; }
    public bool Tout { get; set; }
}
