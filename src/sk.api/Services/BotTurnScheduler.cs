using Microsoft.Extensions.Options;
using sk.shared;

namespace sk.api.Services;

public sealed class BotTurnOptions
{
    public TimeSpan TurnDelay { get; set; } = TimeSpan.FromMilliseconds(900);
    public TimeSpan TrickDelay { get; set; } = TimeSpan.FromMilliseconds(2600);
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(50);
    public TimeSpan DisconnectedTableRetention { get; set; } = TimeSpan.FromMinutes(30);
}

/// <summary>One outstanding decision per table. Policy execution never holds the table lock.</summary>
public sealed class BotTurnScheduler(GameHubService games, IGamePolicy policy,
    IOptions<BotTurnOptions> options, ILogger<BotTurnScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pending = new Dictionary<Guid, Task>();
        var settings = options.Value;
        using var timer = new PeriodicTimer(settings.PollInterval);
        try
        {
            do
            {
                foreach (var id in pending.Where(p => p.Value.IsCompleted).Select(p => p.Key).ToArray())
                    pending.Remove(id);
                games.CleanupAbandonedTables(DateTimeOffset.UtcNow, settings.DisconnectedTableRetention);
                foreach (var id in games.GameIds)
                {
                    if (pending.ContainsKey(id)) continue;
                    if (games.GetActiveBotTurn(id) is { } work)
                        pending[id] = Task.Run(() => PlayTurnAsync(work, settings, stoppingToken), stoppingToken);
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            try { await Task.WhenAll(pending.Values); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        }
    }

    private async Task PlayTurnAsync(ScheduledBotTurn work, BotTurnOptions settings, CancellationToken cancellationToken)
    {
        try
        {
            var observation = work.Turn.Observation;
            // Leave enough time for the existing card and trick-clear animations.
            var delay = observation.Phase == GameState.Playing && observation.TrickNumber > 0 &&
                observation.CurrentTrick.All(c => c < 0) ? settings.TrickDelay : settings.TurnDelay;
            await Task.Delay(delay, cancellationToken);
            // Check again after pacing, then validate the same revision when applying.
            if (games.GetActiveBotTurn(work.GameId) is not { } current ||
                current.PlayerId != work.PlayerId || current.Turn.Revision != work.Turn.Revision) return;
            var action = policy.ChooseAction(observation);
            cancellationToken.ThrowIfCancellationRequested();
            if (games.TryApplyBotAction(work.GameId, work.PlayerId, work.Turn.Revision, action))
                await games.BroadcastGame(work.GameId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            // A removed table or failed delivery must not stop other tables or future turns.
            logger.LogError(exception, "Bot turn failed at table {TableId}, revision {Revision}",
                work.GameId, work.Turn.Revision);
        }
    }
}
