using sk.bot;
using sk.shared;

namespace sk.api.Services;

/// <summary>Load one immutable pair at startup. Training/checkpoint changes cannot change a live round.</summary>
public sealed class ConfiguredBotPolicy : IGamePolicy
{
    private readonly NeuralPolicy? neural;
    private readonly HeuristicPolicy fallback = new(BiddingStyle.Conservative);
    private readonly ILogger<ConfiguredBotPolicy> logger;
    private long fallbackCount;
    public string Id => neural?.Id ?? fallback.Id;
    public bool Trained => neural != null;
    public long FallbackCount => Interlocked.Read(ref fallbackCount);

    public ConfiguredBotPolicy(IConfiguration configuration, ILogger<ConfiguredBotPolicy> logger)
    {
        this.logger = logger;
        string? path = configuration["Bots:ModelManifest"];
        if (!string.IsNullOrWhiteSpace(path)) neural = new NeuralPolicy(path);
        logger.LogInformation("Bot policy loaded: {PolicyId}", Id);
    }

    public GameAction ChooseAction(GameObservation observation)
    {
        if (neural == null) return fallback.ChooseAction(observation);
        try { return neural.ChooseAction(observation); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            Interlocked.Increment(ref fallbackCount);
            logger.LogWarning(error, "Neural policy failed; using conservative fallback for {PolicyId}", Id);
            return fallback.ChooseAction(observation);
        }
    }
}
