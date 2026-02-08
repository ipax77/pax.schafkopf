
namespace sk.shared.Interfaces;

public interface IGameHubClient
{
    PublicGameState? GameState { get; }

    event Action? OnStateChanged;
    event Action? OnGameInitialized;

    ValueTask DisposeAsync();
    Task JoinByCode(Player player, string code);
    Task PlayCard(Card card);
    Task StartAsync(Player player, Guid gameId);
    Task SubmitBidding1(bool wouldPlay);
    Task SubmitBidding2(Bidding2Result? result);
    Task LeaveTable();
    Task Ready();
}