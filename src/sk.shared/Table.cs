
namespace sk.shared;

public sealed class Table
{
    public Guid Guid { get; set; }
    public TablePlayer[] Players { get; set; } = [
        new TablePlayer() { Position = 0 },
        new TablePlayer() { Position = 1 },
        new TablePlayer() { Position = 2 },
        new TablePlayer() { Position = 3 },
    ];
    public PlayerCard? CurrentTrickCard { get; set; }
    public Card?[]? PreviouseTrick { get; set; }
    public Card?[] CurrentTrick { get; set; } = [null, null, null, null];

    public void Reset()
    {
        foreach (var player in Players)
        {
            player.Reset();
        }
        CurrentTrickCard = null;
    }
}
