using System.Text.Json;
using sk.bot;
using sk.shared;

namespace sk.training;

/// <summary>Bounded stdio batches for a sample evaluation. Python selects actions; C# owns all rules.</summary>
public static class NeuralEvaluation
{
    private sealed record Match(int SeedIndex, int Focal, HeadlessGame Game, int ReferencePayout);
    public static void Run(int seeds, ulong master, ulong start)
    {
        if (seeds < 2 || seeds > 10000) throw new ArgumentException("Use 2..10000 evaluation seeds");
        var teacher = new HeuristicPolicy(BiddingStyle.Conservative);
        double[] candidate = new double[seeds], reference = new double[seeds];
        var contracts = new Dictionary<string, int>();
        int completed = 0, decisions = 0, batchId = 0;
        Write(new { kind = "hello", protocol = 1, encoder_version = ObservationEncoder.Version,
            rules_version = HeadlessGame.RulesVersion, feature_count = ObservationEncoder.FeatureCount });
        // All four seats/dealers are retained in one bootstrap block per seed.
        for (int first = 0; first < seeds * 16; first += 64)
        {
            var active = new List<Match>();
            for (int index = first; index < Math.Min(first + 64, seeds * 16); index++)
            {
                int n = index / 16, dealer = index / 4 % 4, focal = index % 4;
                ulong seed = SeedSchedule.Deal(master, SeedSplit.Validation, start + (ulong)n);
                var baseline = EpisodeRunner.Play(seed, dealer, 0, ["conservative", "conservative", "conservative", "conservative"]);
                var game = new HeadlessGame(); game.Reset(seed, dealer);
                active.Add(new(n, focal, game, baseline.Rewards[focal]));
            }
            while (active.Count > 0)
            {
                foreach (var match in active.ToArray())
                {
                    while (!match.Game.IsTerminal && match.Game.CurrentPlayer != match.Focal)
                    {
                        var result = match.Game.Step(teacher.ChooseAction(match.Game.Observe(match.Game.CurrentPlayer)));
                        if (result.Terminal) Finish(match, result);
                    }
                }
                if (active.Count == 0) break;
                var observations = active.Select(m => m.Game.Observe(m.Focal)).ToArray();
                Write(new { kind = "act", batch_id = ++batchId, requests = observations.Select(o => new
                {
                    model = ObservationEncoder.ModelKind(o), features = ObservationEncoder.Encode(o),
                    legal = o.LegalActions.Select(a => ObservationEncoder.LocalAction(o, a)).ToArray()
                }).ToArray() });
                using var response = JsonDocument.Parse(Console.ReadLine() ?? throw new IOException("Inference process disconnected"));
                if (response.RootElement.GetProperty("batch_id").GetInt32() != batchId) throw new InvalidOperationException("Stale inference batch");
                var actions = response.RootElement.GetProperty("actions").EnumerateArray().Select(x => x.GetInt32()).ToArray();
                if (actions.Length != active.Count) throw new InvalidOperationException("Inference batch size mismatch");
                var current = active.ToArray();
                for (int i = 0; i < current.Length; i++)
                {
                    var action = ObservationEncoder.EngineAction(ObservationEncoder.ModelKind(observations[i]), actions[i]);
                    if (!observations[i].LegalActions.Contains(action)) throw new InvalidOperationException("Illegal neural action");
                    if (observations[i].History.Count >= 40) throw new InvalidOperationException("Evaluation episode stalled");
                    var result = current[i].Game.Step(action);
                    decisions++;
                    if (result.Terminal) Finish(current[i], result);
                }
            }
            Write(new { kind = "progress", completed, total = seeds * 16 });

            void Finish(Match match, StepResult result)
            {
                if (result.Rewards.Sum() != 0) throw new InvalidOperationException("Invalid payout conservation");
                var final = match.Game.Observe(match.Focal);
                if (final.Contract != null && (final.Points.Sum() != 120 || final.TrickCounts.Sum() != 8))
                    throw new InvalidOperationException("Invalid card conservation");
                candidate[match.SeedIndex] += result.Rewards[match.Focal] / 16.0;
                reference[match.SeedIndex] += match.ReferencePayout / 16.0;
                string key = final.Contract is { } c ? $"{c.GameType}/{(c.Tout ? "tout" : "normal")}" : "all-pass";
                contracts[key] = contracts.GetValueOrDefault(key) + 1;
                completed++; active.Remove(match);
            }
        }
        Write(new { kind = "result", report = new
        {
            schema_version = 1, rules_version = HeadlessGame.RulesVersion, encoder_version = ObservationEncoder.Version,
            master_seed = master, split = "validation", start_index = start, seeds, completed_games = completed * 2,
            neural_decisions = decisions, illegal_actions = 0, selection = "masked-argmax; lowest local action breaks ties",
            candidate_payout = BlockStatistics.Bootstrap(candidate, 2000, 712367),
            paired_improvement = BlockStatistics.Bootstrap(candidate.Zip(reference, (c, r) => c - r).ToArray(), 2000, 712367),
            candidate_contracts = contracts, blocks = Enumerable.Range(0, seeds).Select(n => new
                { index = start + (ulong)n, candidate = candidate[n], reference = reference[n], difference = candidate[n] - reference[n] }),
            purpose = "small imitation smoke evaluation; not a promotion suite"
        } });
    }
    private static void Write(object message) { Console.WriteLine(JsonSerializer.Serialize(message, TrainingJson.Options)); Console.Out.Flush(); }
}
