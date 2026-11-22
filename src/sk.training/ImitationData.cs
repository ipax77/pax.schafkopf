using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using sk.bot;
using sk.shared;

namespace sk.training;

public static class TrainingJson
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
}

public sealed record DataShard(string File, string Split, string Model, int Rows, string Sha256);

/// <summary>Bounded demonstration export; only one complete episode is buffered for terminal labels.</summary>
public static class ImitationData
{
    public static void Generate(string output, int trainingGames, int validationGames, ulong master, string teacherName = "conservative", int tacticalCopies = 1)
    {
        if (trainingGames < 1 || validationGames < 1 || tacticalCopies is < 1 or > 16 ||
            (teacherName == "conservative" && tacticalCopies != 1)) throw new ArgumentException("Invalid imitation sizes or tactical weighting");
        Directory.CreateDirectory(output);
        using var reservation = new FileStream(Path.Combine(output, "generation.lock"), FileMode.CreateNew);
        var shards = new List<DataShard>();
        if (teacherName is not ("conservative" or "gameplay" or "gameplay-playing")) throw new ArgumentException("Unsupported imitation teacher");
        var teacher = PolicyCatalog.Create(teacherName, 0);
        var timer = Stopwatch.StartNew();
        int total = trainingGames + validationGames, completed = 0, allPass = 0;
        int tacticalRowsAdded = 0;
        foreach (var (split, count) in new[] { (SeedSplit.Training, trainingGames), (SeedSplit.Validation, validationGames) })
        {
            string splitName = split.ToString().ToLowerInvariant();
            using var bidding = new ShardWriter(output, splitName, "bidding", shards);
            using var playing = new ShardWriter(output, splitName, "playing", shards);
            using var traces = new StreamWriter(Path.Combine(output, $"{splitName}-traces.jsonl"));
            var game = new HeadlessGame();
            for (int n = 0; n < count; n++)
            {
                ulong seed = SeedSchedule.Deal(master, split, (ulong)n);
                game.Reset(seed, n % 4);
                var decisions = new List<(int Seat, string Kind, float[] Features, uint Legal, int Action, bool Tactical)>(40);
                var actions = new List<int>(40);
                StepResult result = new(false, new int[4]);
                while (!game.IsTerminal)
                {
                    if (decisions.Count >= 40) throw new InvalidOperationException("Demonstration episode stalled");
                    var observation = game.Observe(game.CurrentPlayer);
                    var action = teacher.ChooseAction(observation);
                    if (!observation.LegalActions.Contains(action)) throw new InvalidOperationException("Illegal teacher action");
                    decisions.Add((game.CurrentPlayer, ObservationEncoder.ModelKind(observation), ObservationEncoder.Encode(observation),
                        ObservationEncoder.LegalMask(observation), ObservationEncoder.LocalAction(observation, action),
                        teacherName != "conservative" && GameplayPolicy.PlayAdvice(observation)?.Reason == "preserve-secure-partner-winner"));
                    actions.Add(action.Id);
                    result = game.Step(action);
                }
                if (result.Rewards.Sum() != 0) throw new InvalidOperationException("Non-zero-sum result");
                if (game.Observe(0).Contract == null) allPass++;
                foreach (var decision in decisions)
                {
                    int copies = split == SeedSplit.Training && decision.Tactical ? tacticalCopies : 1;
                    tacticalRowsAdded += copies - 1;
                    for (int copy = 0; copy < copies; copy++)
                    (decision.Kind == "bidding" ? bidding : playing).Write(decision.Features, decision.Legal,
                        decision.Action, result.Rewards[decision.Seat] / 100f);
                }
                traces.WriteLine(JsonSerializer.Serialize(new { seed, dealer = n % 4, actions, rewards = result.Rewards }, TrainingJson.Options));
                completed++;
                if (completed % 100 == 0 || n == count - 1)
                {
                    double rate = completed / Math.Max(timer.Elapsed.TotalSeconds, 0.001);
                    Console.Error.WriteLine($"DATA {completed}/{total} games ({100.0 * completed / total:F1}%) | {rate:F0} games/s | ETA {(total - completed) / rate:F0}s");
                }
            }
        }
        var manifest = new
        {
            schema_version = 1, encoder_version = ObservationEncoder.Version, rules_version = HeadlessGame.RulesVersion,
            seed_schedule_version = SeedSchedule.Version, teacher = PolicyCatalog.Version(teacherName), master_seed = master,
            training_games = trainingGames, validation_games = validationGames, all_pass_games = allPass,
            tactical_training_copies = tacticalCopies, tactical_training_rows_added = tacticalRowsAdded,
            feature_count = ObservationEncoder.FeatureCount, feature_blocks = ObservationEncoder.Blocks,
            row_format = "little-endian: float32[feature_count], uint32 legal_mask, int32 local_action, float32 payout/100",
            reward_scale = 100, shards, elapsed_seconds = timer.Elapsed.TotalSeconds
        };
        File.WriteAllText(Path.Combine(output, "dataset.json.tmp"), JsonSerializer.Serialize(manifest, TrainingJson.Options));
        File.Move(Path.Combine(output, "dataset.json.tmp"), Path.Combine(output, "dataset.json"));
    }

    private sealed class ShardWriter(string directory, string split, string model, List<DataShard> shards) : IDisposable
    {
        private BinaryWriter? writer;
        private string? filename;
        private int rows, index;
        public void Write(float[] features, uint legal, int action, float value)
        {
            if (writer == null)
            {
                filename = $"{split}-{model}-{index++:D5}.bin";
                writer = new BinaryWriter(new FileStream(Path.Combine(directory, filename), FileMode.CreateNew));
            }
            foreach (float feature in features) writer.Write(feature);
            writer.Write(legal); writer.Write(action); writer.Write(value);
            if (++rows == 4096) Close();
        }
        private void Close()
        {
            if (writer == null) return;
            writer.Dispose(); writer = null;
            using var file = File.OpenRead(Path.Combine(directory, filename!));
            shards.Add(new(filename!, split, model, rows, Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant()));
            rows = 0;
        }
        public void Dispose() => Close();
    }
}
