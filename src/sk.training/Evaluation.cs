using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using sk.shared;

namespace sk.training;

public sealed record EvaluationOptions
{
    public int Seeds { get; init; } = 500;
    public ulong StartIndex { get; init; }
    public ulong MasterSeed { get; init; } = 20261120;
    public SeedSplit Split { get; init; } = SeedSplit.Validation;
    public string Candidate { get; init; } = "balanced";
    public string Reference { get; init; } = "conservative";
    public string[] Opponents { get; init; } = ["balanced", "balanced", "balanced"];
    public int BootstrapSamples { get; init; } = 2000;
    public ulong BootstrapSeed { get; init; } = 712367;
    public int? ContractAction { get; init; }

    public void Validate()
    {
        if (Seeds < 2 || BootstrapSamples < 100 || Opponents.Length != 3)
            throw new ArgumentException("Need >=2 base seeds, >=100 bootstrap samples, and exactly three opponents.");
        if (StartIndex > (ulong.MaxValue >> 2) - (ulong)(Seeds - 1)) throw new ArgumentOutOfRangeException(nameof(StartIndex));
        _ = SeedSchedule.Deal(MasterSeed, Split, StartIndex);
        foreach (var name in Opponents.Append(Candidate).Append(Reference)) _ = PolicyCatalog.Version(name);
        if (ContractAction is { } id && GameAction.FromId(id).Kind != ActionKind.Bid)
            throw new ArgumentException("Contract action must be a legal bid ID (3..16).");
    }
}

public sealed record SeedBlock(ulong Index, ulong DealSeed, int PairedGames, double CandidateMean, double ReferenceMean)
{
    public double Difference => CandidateMean - ReferenceMean;
}

public sealed class OutcomeTotals
{
    [JsonInclude] public int Games { get; private set; }
    [JsonInclude] public int AllPass { get; private set; }
    [JsonInclude] public int Interests { get; private set; }
    [JsonInclude] public int ToutGames { get; private set; }
    [JsonInclude] public int ToutWins { get; private set; }
    [JsonInclude] public int FocalDeclarations { get; private set; }
    [JsonInclude] public int FocalDeclarationWins { get; private set; }
    [JsonInclude] public long RawPayout { get; private set; }
    [JsonInclude] public Dictionary<string, SliceTotals> ByContractRole { get; private set; } = [];
    public void Add(EpisodeTrace trace, int focal)
    {
        Games++;
        Interests += trace.Actions.Count(a => a == 1);
        RawPayout += trace.Rewards[focal];
        if (trace.Contract == null) AllPass++;
        if (trace.Contract?.Tout == true)
        {
            ToutGames++;
            if (trace.Rewards[trace.Contract.Player] > 0) ToutWins++;
        }
        if (trace.Contract?.Player == focal)
        {
            FocalDeclarations++;
            if (trace.Rewards[focal] > 0) FocalDeclarationWins++;
        }
        string key = trace.Contract is { } c ? $"{c.GameType}/{c.Suit}/{(c.Tout ? "tout" : "normal")}/{trace.Roles[focal]}" : "all-pass";
        if (!ByContractRole.TryGetValue(key, out var slice)) ByContractRole.Add(key, slice = new());
        slice.Add(trace.Rewards[focal]);
    }
}

public sealed class SliceTotals
{
    [JsonInclude] public int Games { get; private set; }
    [JsonInclude] public int Wins { get; private set; }
    [JsonInclude] public long Payout { get; private set; }
    public double MeanPayout => (double)Payout / Games;
    public void Add(int payout) { Games++; Payout += payout; if (payout > 0) Wins++; }
}

public sealed record EvaluationReport(int SchemaVersion, string RulesVersion, string SeedScheduleVersion,
    string Runtime, string BuildSdk, string OS, string? SourceCommit, bool? SourceDirty,
    DateTimeOffset CreatedAtUtc, EvaluationOptions Options, Dictionary<string, string> PolicyVersions,
    string SelectionMode, int CompletedGames, int SkippedSeeds, double ElapsedSeconds, string ActionChecksum,
    Estimate CandidatePayout, Estimate ReferencePayout, Estimate PairedImprovement,
    OutcomeTotals CandidateTotals, OutcomeTotals ReferenceTotals, List<SeedBlock> Blocks,
    List<EpisodeTrace> Examples);

public static class Evaluation
{
    public static EvaluationReport Run(EvaluationOptions options, Action<int>? progress = null,
        Action<EpisodeTrace>? traceSink = null)
    {
        options.Validate();
        var timer = Stopwatch.StartNew();
        var blocks = new List<SeedBlock>();
        var examples = new List<EpisodeTrace>();
        var candidateTotals = new OutcomeTotals();
        var referenceTotals = new OutcomeTotals();
        ulong checksum = 14695981039346656037UL;
        int skipped = 0;
        for (int n = 0; n < options.Seeds; n++)
        {
            ulong index = options.StartIndex + (ulong)n;
            ulong seed = SeedSchedule.Deal(options.MasterSeed, options.Split, index);
            int declarer = (int)(index % 4);
            if (options.ContractAction is { } contract && EpisodeRunner.ControlledAuction(seed, 0, declarer, contract) == null)
            {
                skipped++;
                continue;
            }
            double candidateSum = 0, referenceSum = 0;
            for (int dealer = 0; dealer < 4; dealer++)
            {
                int[]? auction = options.ContractAction is { } bid ? EpisodeRunner.ControlledAuction(seed, dealer, declarer, bid) : null;
                for (int focal = 0; focal < 4; focal++)
                {
                    var lineup = new string[4];
                    for (int offset = 1; offset < 4; offset++) lineup[(focal + offset) % 4] = options.Opponents[offset - 1];
                    ulong sampling = SeedSchedule.Sampling(options.MasterSeed, index, dealer);
                    lineup[focal] = options.Candidate;
                    var candidate = EpisodeRunner.Play(seed, dealer, sampling, lineup, auction);
                    lineup[focal] = options.Reference;
                    var reference = EpisodeRunner.Play(seed, dealer, sampling, lineup, auction);
                    candidateSum += candidate.Rewards[focal];
                    referenceSum += reference.Rewards[focal];
                    candidateTotals.Add(candidate, focal);
                    referenceTotals.Add(reference, focal);
                    foreach (var trace in new[] { candidate, reference })
                    {
                        traceSink?.Invoke(trace);
                        if (examples.Count < 2) examples.Add(trace);
                        Mix(trace.Seed); Mix((ulong)trace.Dealer); Mix((ulong)focal);
                        foreach (int action in trace.Actions) Mix((ulong)action);
                        foreach (int reward in trace.Rewards) Mix(unchecked((ulong)reward));
                    }
                }
            }
            blocks.Add(new(index, seed, 16, candidateSum / 16, referenceSum / 16));
            progress?.Invoke(n + 1);
        }
        if (blocks.Count < 2)
            throw new InvalidOperationException($"Only {blocks.Count} eligible base seeds out of {options.Seeds}; need at least two. Check the controlled contract or use an appropriate seed range.");
        Estimate Stats(Func<SeedBlock, double> selector) => BlockStatistics.Bootstrap(blocks.Select(selector).ToArray(),
            options.BootstrapSamples, options.BootstrapSeed);
        // All rotations and both sides stay together: one bootstrap observation per deal seed.
        var candidateEstimate = Stats(b => b.CandidateMean);
        var referenceEstimate = Stats(b => b.ReferenceMean);
        var difference = Stats(b => b.Difference);
        var versionMap = options.Opponents.Append(options.Candidate).Append(options.Reference).Distinct()
            .ToDictionary(name => name, PolicyCatalog.Version);
        return new(1, HeadlessGame.RulesVersion, SeedSchedule.Version, RuntimeInformation.FrameworkDescription,
            typeof(Evaluation).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "BuildSdk").Value!,
            RuntimeInformation.OSDescription, Git("rev-parse", "HEAD"), Git("status", "--porcelain") is { } status ? status.Length > 0 : null,
            DateTimeOffset.UtcNow, options, versionMap, "heuristic: deterministic, action-ID tie-break; neural: masked argmax, action-ID tie-break; random: uniform legal, seeded SplitMix64",
            candidateTotals.Games + referenceTotals.Games, skipped, timer.Elapsed.TotalSeconds, checksum.ToString("X16"),
            candidateEstimate, referenceEstimate, difference, candidateTotals, referenceTotals, blocks, examples);

        void Mix(ulong value) => checksum = unchecked((checksum ^ value) * 1099511628211UL);
    }

    private static string? Git(params string[] args)
    {
        try
        {
            var info = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var arg in args) info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }
            errors.GetAwaiter().GetResult();
            return process.ExitCode == 0 ? output.GetAwaiter().GetResult().Trim() : null;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }
}
