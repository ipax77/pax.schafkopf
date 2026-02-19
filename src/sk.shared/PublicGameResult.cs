namespace sk.shared;

public sealed class PublicGameResult
{
    public List<List<Card>> StartingHands { get; set; } = [];
    public List<int> PlayerCashes { get; set; } = [];
    public GameResult GameResult { get; set; } = new();
}
