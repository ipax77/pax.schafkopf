using System.Diagnostics;
using System.Text.Json;
using sk.bot;
using sk.shared;

namespace sk.training;

public static class NativePolicyAudit
{
    public static void Run(string manifest, string output)
    {
        var neural = new NeuralPolicy(manifest);
        var teacher = new HeuristicPolicy(BiddingStyle.Conservative);
        var observations = new List<GameObservation>();
        for (ulong seed = 0; observations.Count < 1000; seed++)
        {
            var game = new HeadlessGame(); game.Reset(seed);
            while (!game.IsTerminal)
            {
                var own = game.Observe(game.CurrentPlayer);
                if (own.LegalActions.Count > 1) observations.Add(own);
                game.Step(teacher.ChooseAction(own));
            }
        }
        foreach (var observation in observations.Take(100)) neural.ChooseAction(observation);
        var results = new List<object>();
        foreach (int workers in new[] { 1, 10 })
        {
            var elapsed = new double[workers * 1000];
            var wall = Stopwatch.StartNew();
            Parallel.For(0, workers, worker =>
            {
                for (int i = 0; i < 1000; i++)
                {
                    var observation = observations[(i + worker * 17) % observations.Count];
                    long started = Stopwatch.GetTimestamp();
                    var action = neural.ChooseAction(observation);
                    elapsed[worker * 1000 + i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    if (!observation.LegalActions.Contains(action)) throw new InvalidOperationException("Illegal native decision");
                }
            });
            Array.Sort(elapsed);
            results.Add(new { workers, decisions = elapsed.Length, wall_seconds = wall.Elapsed.TotalSeconds,
                mean_ms = elapsed.Average(), p50_ms = elapsed[elapsed.Length / 2], p95_ms = elapsed[(int)(elapsed.Length * .95)], max_ms = elapsed[^1] });
        }
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            policy = neural.Id, runtime = Environment.Version.ToString(), processor_count = Environment.ProcessorCount,
            illegal_actions = 0, observations = observations.Count, measurements = results,
            scope = "Encoding + masked CPU policy decision on non-forced observations; includes CPU contention, excludes scheduler queue/display pacing/network. Not a live-table soak."
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(output);
    }
}
