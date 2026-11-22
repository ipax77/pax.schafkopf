using System.Diagnostics;
using System.Runtime.InteropServices;
using sk.shared;

int count = args.Length == 0 ? 10_000 : int.Parse(args[0]);
if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
Console.WriteLine($"{RuntimeInformation.OSDescription}; {RuntimeInformation.FrameworkDescription}; {Environment.ProcessorCount} logical CPUs; {HeadlessGame.RulesVersion}");
Run(2_000, false);
Run(count, true);

static void Run(int episodes, bool report)
{
    var rng = new Random(777);
    var engine = new HeadlessGame();
    long actions = 0, completed = 0, passed = 0, tout = 0;
    var contracts = new Dictionary<GameType, int>();
    GC.Collect();
    long allocated = GC.GetAllocatedBytesForCurrentThread();
    int gen0 = GC.CollectionCount(0);
    var watch = Stopwatch.StartNew();
    for (int episode = 0; episode < episodes; episode++)
    {
        engine.Reset((ulong)episode, episode % 4);
        int interested = (episode % 4) switch { 0 => 0, 1 => 1, 2 => 2, _ => 4 };
        StepResult? result = null;
        for (int p = 0; p < 4; p++)
        {
            result = engine.Step(GameAction.Interest(p < interested));
            actions++;
        }
        int episodeSteps = 4;
        while (!engine.IsTerminal)
        {
            var observation = engine.Observe(engine.CurrentPlayer);
            var legal = observation.LegalActions;
            if (legal.Count == 0) throw new InvalidOperationException($"Empty legal actions: seed={episode}");
            result = engine.Step(legal[rng.Next(legal.Count)].Id);
            actions++;
            if (++episodeSteps > 40) throw new InvalidOperationException($"Nontermination: seed={episode}");
        }
        var final = engine.Observe(0);
        if (result == null || !result.Terminal || result.Rewards.Sum() != 0)
            throw new InvalidOperationException($"Invalid terminal rewards: seed={episode}");
        if (final.Contract == null)
        {
            passed++;
            if (result.Rewards.Any(r => r != 0)) throw new InvalidOperationException("All-pass reward");
        }
        else
        {
            contracts[final.Contract.GameType] = contracts.GetValueOrDefault(final.Contract.GameType) + 1;
            if (final.Contract.Tout) tout++;
            if (final.TrickNumber != 8 || final.HandCounts.Any(n => n != 0) ||
                final.TrickCounts.Sum() != 8 || final.Points.Sum() != 120 ||
                final.History.Where(e => e.Action.Kind == ActionKind.Play).Select(e => e.Action.CardId).Distinct().Count() != 32)
                throw new InvalidOperationException($"Terminal invariant failed: seed={episode}");
            completed++;
        }
    }
    watch.Stop();
    allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
    if (!report) return;
    Console.WriteLine($"Episodes={episodes}; completed 32-card games={completed}; all-pass={passed}; stalled=0; Tout={tout}");
    Console.WriteLine($"Contracts: {string.Join(", ", contracts.Select(kv => $"{kv.Key}={kv.Value}"))}");
    Console.WriteLine($"Seconds={watch.Elapsed.TotalSeconds:F3}; episodes/s={episodes / watch.Elapsed.TotalSeconds:F0}; completed games/s={completed / watch.Elapsed.TotalSeconds:F0}; actions/s={actions / watch.Elapsed.TotalSeconds:F0}");
    Console.WriteLine($"Allocated bytes/episode={allocated / episodes}; Gen0 collections={GC.CollectionCount(0) - gen0}");
    Console.WriteLine("Seeded deals; detached policy observations; validated actions; bounded episode history. No network, tensor encoding, inference, or training.");
}
