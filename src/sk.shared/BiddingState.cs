
namespace sk.shared;

public sealed class BiddingState
{
    public bool WouldPlay { get; set; }
    public GameType? ProposedGame { get; set; }
    public Suit? ProposedSuit { get; set; }
    public bool Sie { get; set; }
    public bool Tout { get; set; }
}
