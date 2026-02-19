
namespace sk.shared;

public sealed class PlayerCard
{
    public int Position { get; init; }
    public Card Card { get; init; } = new();
}
