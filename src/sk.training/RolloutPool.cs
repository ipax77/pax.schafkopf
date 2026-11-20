using System.Text.Json;
using System.Text.Json.Serialization;
using sk.bot;
using sk.shared;

namespace sk.training;

public sealed record RolloutOptions(string Model, int Target, int PoolSize, int MaxGames,
    ulong Master, ulong Start, string BiddingVersion, string PlayingVersion)
{
    public void Validate()
    {
        if (Model is not ("bidding" or "playing") || Target < 1 || PoolSize is < 1 or > 256 || MaxGames < PoolSize ||
            string.IsNullOrWhiteSpace(BiddingVersion) || string.IsNullOrWhiteSpace(PlayingVersion))
            throw new ArgumentException("Invalid rollout options");
        if (Start > (ulong.MaxValue >> 2) - (ulong)MaxGames) throw new ArgumentOutOfRangeException(nameof(Start));
    }
}

public sealed record RolloutDecision(string EpisodeId, int Decision, int Seat, int Phase, string Model, bool Learner,
    string PolicyVersion, float[] Features, int[] Legal, [property: JsonIgnore] GameObservation Observation);
public sealed record RolloutAction(string EpisodeId, int Decision, int Action, double LogProbability, float Value, string PolicyVersion);
public sealed record RolloutCompletion(string EpisodeId, EpisodeTrace Trace);
public sealed record RolloutBatch(int BatchId, RolloutDecision[] Requests, RolloutCompletion[] Completed,
    int LearnerDecisions, int CompletedGames, int LaunchedGames, bool Draining, bool Done, ulong NextIndex);

/// <summary>Single-owner pool. Finish every game and stop replacing games once the selected decision target is reached.</summary>
public sealed class RolloutPool
{
    private sealed class Episode(ulong index, ulong seed, int dealer, bool[] learnerSeats, string[] versions)
    {
        public string Id { get; } = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public ulong Seed { get; } = seed;
        public int Dealer { get; } = dealer;
        public bool[] LearnerSeats { get; } = learnerSeats;
        public string[] Versions { get; } = versions;
        public HeadlessGame Game { get; } = new();
        public List<int> Actions { get; } = [];
        public int[] Rewards { get; set; } = new int[4];
    }

    private readonly RolloutOptions options;
    private readonly HeuristicPolicy heuristic = new(BiddingStyle.Conservative);
    private readonly List<Episode> active = [];
    private RolloutDecision[]? pending;
    private int batchId, launched, completed, learnerDecisions;

    public RolloutPool(RolloutOptions options)
    {
        options.Validate(); this.options = options;
    }

    public RolloutBatch ReadBatch()
    {
        if (pending != null) throw new InvalidOperationException("Previous batch is still outstanding");
        var finished = new List<RolloutCompletion>();
        foreach (var episode in active.ToArray())
        {
            if (episode.Game.IsTerminal)
            {
                var observation = episode.Game.Observe(0);
                if (episode.Rewards.Sum() != 0 || (observation.Contract != null &&
                    (observation.Points.Sum() != 120 || observation.TrickCounts.Sum() != 8)))
                    throw new InvalidOperationException("Terminal conservation failure");
                finished.Add(new(episode.Id, Trace(episode)));
                active.Remove(episode); completed++;
            }
        }
        bool draining = learnerDecisions >= options.Target;
        // Deliver the last complete traces before failing a no-progress collection on
        // the next read. An all-pass game cap must not lose the final batch's evidence.
        if (!draining && launched >= options.MaxGames && active.Count == 0 && finished.Count > 0)
            return new(++batchId, [], finished.ToArray(), learnerDecisions, completed, launched,
                false, false, options.Start + (ulong)launched);
        while (!draining && active.Count < options.PoolSize)
        {
            if (launched >= options.MaxGames)
            {
                if (active.Count == 0) throw new InvalidOperationException("Rollout exceeded game cap before reaching its decision target");
                break;
            }
            ulong index = options.Start + (ulong)launched++;
            int focal = (int)(index / 4 % 4);
            // Half self-play; half one learner with three frozen heuristics. No historical pool yet.
            // Rotate all four dealers and focal seats independently of table composition.
            bool[] seats = Enumerable.Range(0, 4).Select(s => index / 16 % 2 == 0 || s == focal).ToArray();
            var versions = seats.Select(learner => learner
                ? $"{options.BiddingVersion}|{options.PlayingVersion}"
                : $"{heuristic.Id}|{(options.Model == "bidding" ? options.PlayingVersion : heuristic.Id)}").ToArray();
            var episode = new Episode(index, SeedSchedule.Deal(options.Master, SeedSplit.Training, index), (int)(index % 4), seats, versions);
            episode.Game.Reset(episode.Seed, episode.Dealer);
            active.Add(episode);
        }
        var requests = new List<RolloutDecision>();
        foreach (var episode in active)
        {
            while (!episode.Game.IsTerminal)
            {
                var observation = episode.Game.Observe(episode.Game.CurrentPlayer);
                string kind = ObservationEncoder.ModelKind(observation);
                bool learner = episode.LearnerSeats[observation.Seat];
                // Bidding training uses the same frozen neural player in every seat.
                if (learner || (options.Model == "bidding" && kind == "playing"))
                {
                    requests.Add(new(episode.Id, episode.Actions.Count, observation.Seat, (int)observation.Phase, kind,
                        learner && kind == options.Model, kind == "bidding" ? options.BiddingVersion : options.PlayingVersion,
                        ObservationEncoder.Encode(observation), observation.LegalActions.Select(a => ObservationEncoder.LocalAction(observation, a)).ToArray(), observation));
                    break;
                }
                Step(episode, heuristic.ChooseAction(observation));
            }
        }
        pending = requests.Count > 0 ? requests.ToArray() : null;
        return new(++batchId, requests.ToArray(), finished.ToArray(), learnerDecisions, completed, launched,
            draining, active.Count == 0, options.Start + (ulong)launched);
    }

    public void Apply(int responseBatchId, RolloutAction[] responses)
    {
        var requests = pending ?? throw new InvalidOperationException("No outstanding batch");
        if (responseBatchId != batchId || responses.Length != requests.Length) throw new InvalidOperationException("Stale or malformed rollout response");
        // Validate the whole batch before applying any action, including identity/order/version and finite policy outputs.
        for (int i = 0; i < requests.Length; i++)
        {
            var request = requests[i]; var response = responses[i];
            if (request.EpisodeId != response.EpisodeId || request.Decision != response.Decision || request.PolicyVersion != response.PolicyVersion ||
                !request.Legal.Contains(response.Action) || !double.IsFinite(response.LogProbability) || response.LogProbability > 1e-6 || !float.IsFinite(response.Value))
                throw new InvalidOperationException("Invalid rollout decision identity, version, action or statistics");
        }
        for (int i = 0; i < requests.Length; i++)
        {
            var request = requests[i];
            Step(active.Single(e => e.Id == request.EpisodeId), ObservationEncoder.EngineAction(request.Model, responses[i].Action));
            if (request.Learner) learnerDecisions++;
        }
        pending = null;
    }

    private static void Step(Episode episode, GameAction action)
    {
        if (episode.Actions.Count >= 40) throw new InvalidOperationException("Episode exceeded action bound");
        var result = episode.Game.Step(action);
        episode.Actions.Add(action.Id);
        if (result.Terminal) episode.Rewards = result.Rewards.ToArray();
    }

    private EpisodeTrace Trace(Episode episode)
    {
        var final = episode.Game.Observe(0);
        return new(HeadlessGame.RulesVersion, episode.Seed, episode.Dealer, 0,
            episode.LearnerSeats.Select(s => s ? "learner" : "opponent").ToArray(), episode.Versions,
            episode.Actions.ToArray(), episode.Rewards, final.Contract,
            Enumerable.Range(0, 4).Select(s => final.Contract == null ? "all-pass" : s == final.Contract.Player ? "declarer" :
                final.Contract.GameType == GameType.Ruf && s == final.PublicTeammate ? "partner" : "defender").ToArray(), []);
    }

    public EpisodeTrace[] ActiveTraces() => active.Select(Trace).ToArray();

    public static void Serve(RolloutOptions options, string tracePath)
    {
        var pool = new RolloutPool(options);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(tracePath))!);
        using var traces = new StreamWriter(new FileStream(tracePath, FileMode.CreateNew));
        void Write(object message) { Console.WriteLine(JsonSerializer.Serialize(message, TrainingJson.Options)); Console.Out.Flush(); }
        Write(new { kind = "hello", protocol = 2, encoder_version = ObservationEncoder.Version,
            rules_version = HeadlessGame.RulesVersion, feature_count = ObservationEncoder.FeatureCount, options });
        try
        {
            while (true)
            {
                var batch = pool.ReadBatch();
                foreach (var terminal in batch.Completed) traces.WriteLine(JsonSerializer.Serialize(terminal, TrainingJson.Options));
                traces.Flush();
                Write(new { kind = "batch", batch });
                if (batch.Done) break;
                if (batch.Requests.Length == 0) continue;
                using var response = JsonDocument.Parse(Console.ReadLine() ?? throw new IOException("Learner disconnected"));
                var root = response.RootElement;
                pool.Apply(root.GetProperty("batch_id").GetInt32(), root.GetProperty("actions").Deserialize<RolloutAction[]>(TrainingJson.Options)
                    ?? throw new InvalidOperationException("Missing actions"));
            }
        }
        catch (Exception error)
        {
            File.WriteAllText(tracePath + ".failure.json", JsonSerializer.Serialize(
                pool.ActiveTraces().Select(trace => trace with { Failure = error.Message }), TrainingJson.Options));
            throw;
        }
    }
}
