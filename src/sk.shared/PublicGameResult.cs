namespace sk.shared;

public sealed class PublicGameResult
{
    public List<List<Card>> StartingHands { get; set; } = [];
    public List<int> PlayerCashes { get; set; } = [];
    public List<Player> Players { get; set; } = [];
    public List<int> PlayerChanges { get; set; } = [];
    public GameResult GameResult { get; set; } = new();
}
