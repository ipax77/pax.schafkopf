using System.Text.Json;
using sk.shared;
using sk.training;

namespace sk.tests;

[TestClass]
public sealed class EvaluationTests
{
    [TestMethod]
    public void SeedStreamsAreDisjointStableAndBounded()
    {
        var seeds = new HashSet<ulong>();
        foreach (var split in Enum.GetValues<SeedSplit>())
            for (ulong index = 0; index < 1000; index++)
                Assert.IsTrue(seeds.Add(SeedSchedule.Deal(42, split, index)));
        Assert.AreEqual(0xe220a8397b1dcdafUL, new StableRandom(0).NextUInt64());
        Assert.AreNotEqual(SeedSchedule.Sampling(42, 0, 1), SeedSchedule.Sampling(42, 1UL << 61, 0));
        var samplingSeeds = Enumerable.Range(0, 1000).SelectMany(index => Enumerable.Range(0, 4)
            .Select(dealer => SeedSchedule.Sampling(42, (ulong)index, dealer))).ToArray();
        Assert.AreEqual(4000, samplingSeeds.Distinct().Count());
        Assert.Throws<ArgumentOutOfRangeException>(() => SeedSchedule.Deal(42, SeedSplit.Training, ulong.MaxValue));
    }

    [TestMethod]
    public void IdenticalCandidatesHaveZeroPairedDifferenceEvenWithRandomSeats()
    {
        var options = new EvaluationOptions
        {
            Seeds = 8, Candidate = "random", Reference = "random",
            Opponents = ["balanced", "random", "conservative"], BootstrapSamples = 200
        };
        var report = Evaluation.Run(options);
        Assert.AreEqual(8 * 32, report.CompletedGames);
        Assert.AreEqual(8, report.PairedImprovement.IndependentSeeds);
        Assert.AreEqual(0.0, report.PairedImprovement.Mean);
        Assert.AreEqual(0.0, report.PairedImprovement.Lower95);
        Assert.AreEqual(0.0, report.PairedImprovement.Upper95);
        Assert.IsTrue(report.Blocks.All(b => b.PairedGames == 16));
        Assert.AreEqual(report.ActionChecksum, Evaluation.Run(options).ActionChecksum);
    }

    [TestMethod]
    public void EvaluationActuallyCoversEverySeatAndDealerAndClonesLineups()
    {
        var traces = new List<EpisodeTrace>();
        var report = Evaluation.Run(new() { Seeds = 2, BootstrapSamples = 100 }, traceSink: traces.Add);
        Assert.HasCount(64, traces);
        foreach (var group in traces.Chunk(32))
        {
            foreach (int dealer in Enumerable.Range(0, 4)) Assert.AreEqual(8, group.Count(t => t.Dealer == dealer));
            // Every reference has exactly one conservative seat, rotated through all four positions.
            for (int seat = 0; seat < 4; seat++) Assert.AreEqual(4, group.Count(t => t.Policies[seat] == "conservative"));
        }
        Assert.IsTrue(report.Examples[0].Policies.All(p => p == "balanced"));
        foreach (var trace in report.Examples) EpisodeRunner.Replay(trace);
    }

    [TestMethod]
    public void BootstrapCountsBaseSeedsAndHandlesConstantAndVariableSamples()
    {
        var constant = BlockStatistics.Bootstrap([5, 5, 5, 5], 200, 9);
        Assert.AreEqual(new Estimate(4, 5, 5, 5), constant);
        var variable = BlockStatistics.Bootstrap([-10, 10], 1000, 9);
        Assert.AreEqual(0.0, variable.Mean);
        Assert.AreEqual(-10.0, variable.Lower95);
        Assert.AreEqual(10.0, variable.Upper95);
        Assert.Throws<ArgumentException>(() => BlockStatistics.Bootstrap([5], 200, 9));
    }

    [TestMethod]
    public void ControlledContractsReplaySameAuctionAndExcludeIllegalDeals()
    {
        var report = Evaluation.Run(new() { Seeds = 12, ContractAction = 3, BootstrapSamples = 100 });
        Assert.IsGreaterThanOrEqualTo(2, report.Blocks.Count);
        foreach (var trace in report.Examples)
        {
            Assert.AreEqual(GameType.Ruf, trace.Contract!.GameType);
            Assert.AreEqual(Suit.Schellen, trace.Contract.Suit);
            Assert.HasCount(5, trace.AuctionPrefix);
            CollectionAssert.AreEqual(trace.AuctionPrefix, trace.Actions.Take(5).ToArray());
            EpisodeRunner.Replay(trace);
        }
        Assert.AreEqual(report.Blocks.Count * 32, report.CompletedGames);
        Assert.AreEqual(12, report.Blocks.Count + report.SkippedSeeds);
    }

    [TestMethod]
    public void AllPassEndsWithZeroRewardsAndNoPlayActions()
    {
        var trace = EpisodeRunner.Play(42, 3, 1, ["balanced", "balanced", "balanced", "balanced"],
            policyFactory: _ => new FixedPolicy(GameAction.Interest(false)));
        Assert.HasCount(4, trace.Actions);
        Assert.IsNull(trace.Contract);
        Assert.IsTrue(trace.Rewards.All(r => r == 0));
        EpisodeRunner.Replay(trace);
    }

    [TestMethod]
    public void IllegalPolicyActionFailsWithReplayablePrefix()
    {
        var failure = Assert.Throws<EpisodeFailureException>(() => EpisodeRunner.Play(42, 3, 1,
            ["balanced", "balanced", "balanced", "balanced"],
            policyFactory: _ => new FixedPolicy(GameAction.Pass)));
        Assert.AreEqual(42UL, failure.Trace.Seed);
        Assert.IsEmpty(failure.Trace.Actions);
        StringAssert.Contains(failure.Trace.Failure, "illegal action");
        Assert.IsFalse(EpisodeRunner.Replay(failure.Trace, false).Terminal);
        Assert.Throws<InvalidOperationException>(() => EpisodeRunner.Replay(failure.Trace with { RulesVersion = "old" }));
    }

    [TestMethod]
    public void ReportsCanBeSerializedAndReplayed()
    {
        var report = Evaluation.Run(new() { Seeds = 2, BootstrapSamples = 100 });
        var copy = JsonSerializer.Deserialize<EvaluationReport>(JsonSerializer.Serialize(report))!;
        Assert.AreEqual(report.ActionChecksum, copy.ActionChecksum);
        Assert.AreEqual(report.CandidateTotals.Games, copy.CandidateTotals.Games);
        Assert.AreEqual(report.CandidateTotals.RawPayout, copy.CandidateTotals.RawPayout);
        CollectionAssert.AreEquivalent(report.CandidateTotals.ByContractRole.Keys.ToArray(), copy.CandidateTotals.ByContractRole.Keys.ToArray());
        foreach (var key in report.CandidateTotals.ByContractRole.Keys)
            Assert.AreEqual(report.CandidateTotals.ByContractRole[key].Games, copy.CandidateTotals.ByContractRole[key].Games);
        foreach (var trace in copy.Examples) EpisodeRunner.Replay(trace);
    }

    private sealed class FixedPolicy(GameAction action) : IGamePolicy
    {
        public GameAction ChooseAction(GameObservation observation) => action;
    }
}
