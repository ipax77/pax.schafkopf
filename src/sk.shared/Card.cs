
namespace sk.shared;

public sealed record Card
{
    public Rank Rank { get; init; }
    public Suit Suit { get; init; }
}
