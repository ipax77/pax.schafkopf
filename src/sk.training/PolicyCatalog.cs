using sk.bot;
using sk.shared;

namespace sk.training;

public static class PolicyCatalog
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, NeuralPolicy> Neural = new();
    private static NeuralPolicy Load(string name) => Neural.GetOrAdd(Path.GetFullPath(name[7..]), path => new NeuralPolicy(path));
    public static string Version(string name) => name switch
    {
        _ when name.StartsWith("neural:", StringComparison.Ordinal) => Load(name).Id,
        "balanced" => $"{HeuristicPolicy.Version}/balanced",
        "conservative" => $"{HeuristicPolicy.Version}/conservative",
        "gameplay" => GameplayPolicy.Version,
        "gameplay-bidding" => $"{GameplayPolicy.Version}/bidding+{HeuristicPolicy.Version}/conservative-play",
        "gameplay-playing" => $"{HeuristicPolicy.Version}/conservative-bidding+{GameplayPolicy.Version}/playing",
        "random" => "uniform-legal-splitmix64-v1",
        _ => throw new ArgumentException($"Unknown policy '{name}'. Use balanced, conservative, or random.")
    };
    public static IGamePolicy Create(string name, ulong seed) => name switch
    {
        _ when name.StartsWith("neural:", StringComparison.Ordinal) => Load(name),
        "balanced" => new HeuristicPolicy(),
        "conservative" => new HeuristicPolicy(BiddingStyle.Conservative),
        "gameplay" => new GameplayPolicy(),
        "gameplay-bidding" => new FixedBiddingPolicy(new GameplayPolicy(), new HeuristicPolicy(BiddingStyle.Conservative)),
        "gameplay-playing" => new FixedBiddingPolicy(new HeuristicPolicy(BiddingStyle.Conservative), new GameplayPolicy()),
        "random" => new RandomPolicy(seed),
        _ => throw new ArgumentException($"Unknown policy '{name}'.")
    };

    private sealed class RandomPolicy(ulong seed) : IGamePolicy
    {
        private readonly StableRandom random = new(seed);
        public GameAction ChooseAction(GameObservation observation) => observation.LegalActions.Count > 0
            ? observation.LegalActions[random.Next(observation.LegalActions.Count)]
            : throw new InvalidOperationException("No legal action.");
    }
}
