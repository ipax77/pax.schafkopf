using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using sk.training;

var json = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
string? outputPath = null;
try
{
    if (args.Length == 0 || args[0] is "--help" or "help")
    {
        Console.WriteLine("""
            sk.training evaluate --output report.json [--seeds 500] [--start 0] [--master 20261120]
              [--candidate balanced] [--reference conservative]
              [--opponents balanced,balanced,balanced] [--split Validation]
              [--bootstrap 2000] [--bootstrap-seed 712367] [--contract 3..16] [--traces games.jsonl]
            sk.training replay report.json
            sk.training replay-failure report.json.failure.json
            sk.training demonstrate --output directory [--train-games 2000] [--validation-games 256] [--master 20261120]
            sk.training evaluate-neural [--seeds 32] [--master 20261120] [--start 1000000] (batched stdio)
            sk.training rollout --traces file.jsonl --model playing --target 4096 --pool 64
              --master 20261120 --start 2000000 --max-games 16384 --bidding-version id --playing-version id
            sk.training replay-rollout file.jsonl
            sk.training replay-rollout-failure file.jsonl.failure.json
            Policies: balanced, conservative, random. Each base seed produces 32 games.
            Contract mode uses a forced legal auction, rotates declarer across base seeds,
            and skips seeds where that declaration is illegal. All payouts use raw tariff units.
            """);
        return 0;
    }
    if (args[0] is "replay-rollout" or "replay-rollout-failure")
    {
        if (args.Length != 2) throw new ArgumentException("Rollout replay needs one trace path.");
        bool failure = args[0] == "replay-rollout-failure";
        IEnumerable<EpisodeTrace> rolloutTraces = failure
            ? JsonSerializer.Deserialize<EpisodeTrace[]>(File.ReadAllText(args[1]), TrainingJson.Options)
                ?? throw new ArgumentException("Missing failure traces")
            : File.ReadLines(args[1]).Select(line => (JsonSerializer.Deserialize<RolloutCompletion>(line, TrainingJson.Options)
                ?? throw new ArgumentException("Missing rollout trace")).Trace);
        int count = 0;
        foreach (var trace in rolloutTraces) { EpisodeRunner.Replay(trace, requireTerminal: !failure); count++; }
        Console.WriteLine($"Replayed {count} rollout {(failure ? "accepted prefixes" : "complete games with identical payouts")}.");
        return 0;
    }
    if (args[0] == "rollout")
    {
        string[] allowed = ["--traces", "--model", "--target", "--pool", "--master", "--start", "--max-games", "--bidding-version", "--playing-version"];
        var values = new Dictionary<string, string>();
        for (int i = 1; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !allowed.Contains(args[i]) || !values.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException($"Invalid or duplicate option: {args[i]}");
        }
        string Required(string key) => values.GetValueOrDefault(key) ?? throw new ArgumentException($"Missing {key}");
        int Number(string key) => int.Parse(Required(key), CultureInfo.InvariantCulture);
        ulong Large(string key) => ulong.Parse(Required(key), CultureInfo.InvariantCulture);
        RolloutPool.Serve(new(Required("--model"), Number("--target"), Number("--pool"), Number("--max-games"),
            Large("--master"), Large("--start"), Required("--bidding-version"), Required("--playing-version")), Required("--traces"));
        return 0;
    }
    if (args[0] is "demonstrate" or "evaluate-neural")
    {
        var allowed = args[0] == "demonstrate"
            ? new[] { "--output", "--train-games", "--validation-games", "--master" }
            : new[] { "--seeds", "--master", "--start" };
        var values = new Dictionary<string, string>();
        for (int i = 1; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !allowed.Contains(args[i]) || !values.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException($"Invalid or duplicate option: {args[i]}");
        }
        ulong master = ulong.Parse(values.GetValueOrDefault("--master", "20261120"), CultureInfo.InvariantCulture);
        if (args[0] == "demonstrate")
            ImitationData.Generate(values.GetValueOrDefault("--output") ?? throw new ArgumentException("--output required"),
                int.Parse(values.GetValueOrDefault("--train-games", "2000"), CultureInfo.InvariantCulture),
                int.Parse(values.GetValueOrDefault("--validation-games", "256"), CultureInfo.InvariantCulture), master);
        else
            NeuralEvaluation.Run(int.Parse(values.GetValueOrDefault("--seeds", "32"), CultureInfo.InvariantCulture), master,
                ulong.Parse(values.GetValueOrDefault("--start", "1000000"), CultureInfo.InvariantCulture));
        return 0;
    }
    if (args[0] is "replay" or "replay-failure")
    {
        if (args.Length != 2) throw new ArgumentException("Replay needs one report path.");
        if (args[0] == "replay-failure")
        {
            var trace = JsonSerializer.Deserialize<EpisodeTrace>(File.ReadAllText(args[1]), json)!;
            EpisodeRunner.Replay(trace, requireTerminal: false);
            Console.WriteLine($"Replayed {trace.Actions.Length} accepted actions before the failure.");
        }
        else
        {
            var report = JsonSerializer.Deserialize<EvaluationReport>(File.ReadAllText(args[1]), json)!;
            foreach (var trace in report.Examples) EpisodeRunner.Replay(trace);
            Console.WriteLine($"Replayed {report.Examples.Count} example games with identical payouts.");
        }
        return 0;
    }
    if (args[0] != "evaluate") throw new ArgumentException("Expected evaluate, replay, or replay-failure.");
    var options = new EvaluationOptions();
    string? tracesPath = null;
    for (int i = 1; i < args.Length; i += 2)
    {
        if (i + 1 >= args.Length) throw new ArgumentException($"Missing value for {args[i]}.");
        string value = args[i + 1];
        options = args[i] switch
        {
            "--seeds" => options with { Seeds = int.Parse(value, CultureInfo.InvariantCulture) },
            "--start" => options with { StartIndex = ulong.Parse(value, CultureInfo.InvariantCulture) },
            "--master" => options with { MasterSeed = ulong.Parse(value, CultureInfo.InvariantCulture) },
            "--split" => options with { Split = Enum.Parse<SeedSplit>(value, true) },
            "--candidate" => options with { Candidate = value },
            "--reference" => options with { Reference = value },
            "--opponents" => options with { Opponents = value.Split(',', StringSplitOptions.TrimEntries) },
            "--bootstrap" => options with { BootstrapSamples = int.Parse(value, CultureInfo.InvariantCulture) },
            "--bootstrap-seed" => options with { BootstrapSeed = ulong.Parse(value, CultureInfo.InvariantCulture) },
            "--contract" => options with { ContractAction = int.Parse(value, CultureInfo.InvariantCulture) },
            "--output" or "--traces" => options,
            _ => throw new ArgumentException($"Unknown option {args[i]}.")
        };
        if (args[i] == "--output") outputPath = Path.GetFullPath(value);
        if (args[i] == "--traces") tracesPath = Path.GetFullPath(value);
    }
    if (outputPath == null) throw new ArgumentException("--output is required.");
    if (outputPath == tracesPath) throw new ArgumentException("Report and trace paths must differ.");
    options.Validate();
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    if (tracesPath != null) Directory.CreateDirectory(Path.GetDirectoryName(tracesPath)!);
    using var traces = tracesPath == null ? null : new StreamWriter(tracesPath);
    var compact = new JsonSerializerOptions(json) { WriteIndented = false };
    var result = Evaluation.Run(options, n => { if (n % 100 == 0) Console.Error.WriteLine($"{n}/{options.Seeds} base seeds"); },
        traces == null ? null : trace => traces.WriteLine(JsonSerializer.Serialize(trace, compact)));
    File.WriteAllText(outputPath + ".tmp", JsonSerializer.Serialize(result, json) + Environment.NewLine);
    File.Move(outputPath + ".tmp", outputPath, overwrite: true);
    Console.WriteLine($"{result.CompletedGames:N0} games; {result.Blocks.Count} independent seeds; {result.SkippedSeeds} ineligible seeds; checksum {result.ActionChecksum}");
    Console.WriteLine($"Candidate payout: {result.CandidatePayout.Mean:F3} [{result.CandidatePayout.Lower95:F3}, {result.CandidatePayout.Upper95:F3}]");
    Console.WriteLine($"Paired improvement: {result.PairedImprovement.Mean:F3} [{result.PairedImprovement.Lower95:F3}, {result.PairedImprovement.Upper95:F3}] (95% base-seed bootstrap CI)");
    Console.WriteLine(outputPath);
    return 0;
}
catch (EpisodeFailureException error)
{
    if (outputPath != null) File.WriteAllText(outputPath + ".failure.json", JsonSerializer.Serialize(error.Trace, json));
    Console.Error.WriteLine(error.Message);
    return 1;
}
catch (Exception error) when (error is ArgumentException or FormatException or OverflowException or IOException or InvalidOperationException or JsonException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
