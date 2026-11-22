using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using sk.shared;

// Dependency-free, repeated batch benchmark. Setup is outside timing except Reset/Deal,
// auction, and full episodes, where setup is deliberately part of the measured workload.
if (args.Length is < 1 or > 2)
    throw new ArgumentException("Usage: sk.benchmarks output.json [baseline.json]");
var fixture = new Game();
fixture.ResetEpisode(42);
for (int p = 0; p < 4; p++) fixture.SetBidding1(p, new() { WouldPlay = p == 0 });
fixture.SetBidding2(0, new() { ProposedGame = GameType.Solo, ProposedSuit = Suit.Herz });
fixture.PlayCard(0, fixture.Table.Players[0].Hand[0]);
var hand = fixture.Table.Players[1].Hand;
var contract = fixture.Bidding2Result!;
var lead = fixture.Table.CurrentTrick[0];
var scratch = new Game();
var engine = new HeadlessGame();
var cases = new (string Name, int Operations, Func<int, ulong> Run)[]
{
    ("Deal", 50_000, n => Repeat(n, i => { scratch.DealCards((ulong)i); return (ulong)GameAction.ToCardId(scratch.Table.Players[0].Hand[0]); })),
    ("LegalCards", 200_000, n => Repeat(n, _ => (ulong)GameRules.LegalCards(hand, contract, lead, false).Count)),
    ("Observe", 50_000, n => Repeat(n, _ => (ulong)fixture.Observe(1).LegalActions.Count)),
    ("Auction", 10_000, n => Auctions(scratch, n)),
    ("FullGame", 10_000, n => Games(engine, n, false)),
    ("FullGameWithObservations", 10_000, n => Games(engine, n, true))
};
var results = new List<Measurement>();
foreach (var test in cases)
{
    var warmup = Stopwatch.StartNew();
    do test.Run(test.Operations); while (warmup.Elapsed.TotalSeconds < 1);
    var samples = new List<double>();
    long bytes = 0;
    ulong? expected = null;
    for (int sample = 0; sample < 7; sample++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        ulong checksum = test.Run(test.Operations);
        long end = Stopwatch.GetTimestamp();
        bytes += GC.GetAllocatedBytesForCurrentThread() - before;
        if (expected.HasValue && expected.Value != checksum) throw new Exception("Nondeterministic benchmark");
        expected = checksum;
        samples.Add((end - start) * 1_000_000.0 / Stopwatch.Frequency / test.Operations);
    }
    var sorted = samples.Order().ToArray();
    var result = new Measurement(test.Name, test.Operations, sorted[3], sorted[0], sorted[6],
        bytes / (7.0 * test.Operations), expected!.Value.ToString("X16"), samples);
    results.Add(result);
    Console.WriteLine($"{result.Name,-25} {result.MedianMicroseconds,10:F3} us/op  {result.BytesPerOperation,10:F0} B/op  checksum={result.Checksum}");
}
if (args.Length == 2)
{
    var baseline = JsonSerializer.Deserialize<Report>(File.ReadAllText(args[1]))!;
    foreach (var result in results)
    {
        var before = baseline.Results.Single(r => r.Name == result.Name);
        if (before.Checksum != result.Checksum || before.Operations != result.Operations)
            throw new Exception($"Behavior changed: {result.Name}");
        Console.WriteLine($"{result.Name}: {before.MedianMicroseconds / result.MedianMicroseconds:F2}x throughput; {100 * (1 - result.BytesPerOperation / before.BytesPerOperation):F1}% less allocation");
    }
}
var report = new Report(RuntimeInformation.OSDescription, RuntimeInformation.FrameworkDescription,
    Environment.ProcessorCount, HeadlessGame.RulesVersion, DateTimeOffset.UtcNow, results);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
File.WriteAllText(args[0], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

static ulong Repeat(int n, Func<int, ulong> action)
{
    ulong hash = 0;
    for (int i = 0; i < n; i++) hash = Mix(hash, action(i));
    return hash;
}
static ulong Mix(ulong hash, ulong value) => unchecked((hash ^ value) * 1099511628211UL);
static ulong Auctions(Game game, int n)
{
    ulong hash = 0;
    for (int i = 0; i < n; i++)
    {
        game.ResetEpisode((ulong)i, i % 4);
        for (int p = 0; p < 4; p++) game.Step(game.ActivePlayer, GameAction.Interest(true));
        while (game.GameState == GameState.Bidding2)
        {
            var legal = game.GetLegalActions();
            var action = legal[(i + game.ActivePlayer) % legal.Count];
            hash = Mix(hash, (ulong)action.Id);
            game.Step(game.ActivePlayer, action);
        }
        hash = Mix(hash, (ulong)game.Bidding2Result!.GameType);
        hash = Mix(hash, (ulong)game.Bidding2Result.PlayerIndex);
    }
    return hash;
}
static ulong Games(HeadlessGame engine, int n, bool observe)
{
    ulong hash = 0;
    for (int i = 0; i < n; i++)
    {
        engine.Reset((ulong)i, i % 4);
        uint rng = (uint)i + 1;
        int steps = 0;
        while (!engine.IsTerminal)
        {
            var legal = observe ? engine.Observe(engine.CurrentPlayer).LegalActions : engine.LegalActions();
            foreach (var a in legal) hash = Mix(hash, (ulong)a.Id);
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            // Always have a declarer, so every measured episode is a complete 32-card game.
            var action = steps < 4 ? GameAction.Interest(steps == 0 || (i + steps) % 3 == 0)
                : legal[(int)(rng % (uint)legal.Count)];
            hash = Mix(hash, (ulong)action.Id);
            var result = engine.Step(action);
            foreach (int reward in result.Rewards) hash = Mix(hash, unchecked((ulong)reward));
            if (++steps > 40) throw new Exception("Nonterminating game");
        }
        var final = engine.Observe(0);
        if (final.TrickNumber != 8 || final.Points.Sum() != 120 || final.HandCounts.Any(c => c != 0))
            throw new Exception("Incomplete game");
        foreach (int points in final.Points) hash = Mix(hash, (ulong)points);
    }
    return hash;
}

record Measurement(string Name, int Operations, double MedianMicroseconds, double MinMicroseconds,
    double MaxMicroseconds, double BytesPerOperation, string Checksum, List<double> SamplesMicroseconds);
record Report(string OS, string Runtime, int LogicalProcessors, string RulesVersion, DateTimeOffset Timestamp,
    List<Measurement> Results);
