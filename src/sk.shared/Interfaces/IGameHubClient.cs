
namespace sk.shared.Interfaces;

public interface IGameHubClient
{
    PublicGameState? GameState { get; }

    event Action? OnStateChanged;

    ValueTask DisposeAsync();
    Task PlayCard(Card card);
    Task StartAsync(Uri uri, Player player, Guid gameId);
    Task SubmitBidding1(bool wouldPlay);
    Task SubmitBidding2(Bidding2Result? result);
    Task LeaveTable();
}