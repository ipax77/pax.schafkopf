
namespace sk.shared;

public sealed class TablePlayer
{
    public Player Player { get; set; } = new();
    public byte Position { get; init; }
    public List<Card> StartingHand { get; set; } = [];
    public List<Card> Hand { get; set; } = [];
    public List<Card> Tricks { get; set; } = [];
    public bool IsConnected { get; set; }
    public int Cash { get; set; }
    public bool ReadyForNextRound { get; set; }
}
