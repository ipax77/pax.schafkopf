using System.Text.Json;
using sk.bot;
using sk.shared;

namespace sk.training;

/// <summary>Inspect deployed models' decisions with both one and three neural seats.</summary>
public static class GameplayAudit
{
    private sealed class RecordedPolicy(IGamePolicy policy, GameplayDiagnostics diagnostics) : IGamePolicy
    {
        public GameAction ChooseAction(GameObservation observation)
        {
            var action = policy.ChooseAction(observation);
            diagnostics.Record(observation, action);
            return action;
        }
    }

    public static void Run(string manifest, string output, int seeds)
    {
        if (seeds is < 2 or > 10000) throw new ArgumentException("Use 2..10000 audit seeds");
        string name = "neural:" + Path.GetFullPath(manifest);
        var policy = PolicyCatalog.Create(name, 0);
        var panels = new Dictionary<string, object>();
        foreach (int neuralSeats in new[] { 1, 3 })
        foreach (string opponent in new[] { "conservative", "gameplay" })
        {
            var diagnostics = new GameplayDiagnostics();
            var recorded = new RecordedPolicy(policy, diagnostics);
            var totals = new OutcomeTotals();
            for (int n = 0; n < seeds; n++)
            for (int dealer = 0; dealer < 4; dealer++)
            for (int focal = 0; focal < 4; focal++)
            {
                ulong seed = SeedSchedule.Deal(20261121, SeedSplit.Validation, 6000000 + (ulong)n);
                bool NeuralSeat(int seat) => (seat - focal + 4) % 4 < neuralSeats;
                var lineup = Enumerable.Range(0, 4).Select(s => NeuralSeat(s) ? name : opponent).ToArray();
                var trace = EpisodeRunner.Play(seed, dealer, 0, lineup, policyFactory: s =>
                    NeuralSeat(s) ? recorded : PolicyCatalog.Create(opponent, 0));
                totals.Add(trace, focal);
            }
            panels[$"{neuralSeats}-neural-vs-{opponent}"] = new { totals, guidance = diagnostics.Stats };
        }
        var report = new
        {
            policy = PolicyCatalog.Version(name), seeds, master_seed = 20261121, split = "validation", start_index = 6000000, illegal_actions = 0,
            purpose = "Private-observation gameplay guidance diagnostics; deviations are review candidates, not proof of mistakes or strength",
            panels
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(report, TrainingJson.Options));
        Console.WriteLine(output);
    }
}
