
namespace sk.shared;

public sealed class Bidding1Result
{
    public IReadOnlyList<int> InterestedPlayers { get; init; } = [];
    public IReadOnlyList<int> DeclinedPlayers { get; init; } = [];
}
