using sk.bot;
using sk.shared;
using sk.training;

namespace sk.tests;

[TestClass]
public sealed class RolloutTests
{
    private static RolloutOptions Options(string model = "playing", int target = 200, int pool = 8, int max = 100, ulong start = 0) =>
        new(model, target, pool, max, 20261120, start, "bid-v1", "play-v1");
    private static RolloutAction[] Responses(RolloutBatch batch, Func<RolloutDecision, GameAction>? choose = null) =>
        batch.Requests.Select(r => new RolloutAction(r.EpisodeId, r.Decision,
            ObservationEncoder.LocalAction(r.Observation, choose?.Invoke(r) ?? new HeuristicPolicy(BiddingStyle.Conservative).ChooseAction(r.Observation)),
            -Math.Log(r.Legal.Length), .1f, r.PolicyVersion)).ToArray();

    [TestMethod]
    public void AllPassDeliversEverySeatRewardAndDrainsWithoutReplacingGames()
    {
        var pool = new RolloutPool(Options("bidding", 1, 2, 10));
        var completions = new List<RolloutCompletion>();
        int requests = 0;
        for (int round = 0; round < 20; round++)
        {
            var batch = pool.ReadBatch();
            completions.AddRange(batch.Completed);
            if (batch.Done)
            {
                Assert.AreEqual(2, batch.LaunchedGames);
                Assert.AreEqual(2, batch.CompletedGames);
                Assert.AreEqual(8, batch.LearnerDecisions);
                Assert.AreEqual(2UL, batch.NextIndex);
                Assert.AreEqual(8, requests);
                Assert.HasCount(2, completions);
                foreach (var complete in completions)
                {
                    Assert.IsNull(complete.Trace.Contract);
                    CollectionAssert.AreEqual(new[] { 0, 0, 0, 0 }, complete.Trace.Rewards);
                    Assert.IsTrue(EpisodeRunner.Replay(complete.Trace).Terminal);
                }
                return;
            }
            requests += batch.Requests.Length;
            Assert.IsTrue(batch.Requests.All(r => r.Learner && r.Model == "bidding"));
            pool.Apply(batch.BatchId, Responses(batch, _ => GameAction.Interest(false)));
        }
        Assert.Fail("Pool did not drain");
    }

    [TestMethod]
    public void ResponsesAreValidatedAtomicallyAndCannotBeAppliedTwice()
    {
        var pool = new RolloutPool(Options(pool: 2));
        var batch = pool.ReadBatch();
        var responses = Responses(batch);
        Assert.Throws<InvalidOperationException>(() => pool.ReadBatch());
        Assert.Throws<InvalidOperationException>(() => pool.Apply(batch.BatchId + 1, responses));
        foreach (var invalid in new[] {
            responses[1] with { Decision = 99 }, responses[1] with { EpisodeId = "missing" },
            responses[1] with { PolicyVersion = "other-weights" }, responses[1] with { Action = 48 },
            responses[1] with { LogProbability = double.NaN }, responses[1] with { LogProbability = 1 },
            responses[1] with { Value = float.PositiveInfinity } })
        {
            Assert.Throws<InvalidOperationException>(() => pool.Apply(batch.BatchId, [responses[0], invalid]));
            Assert.IsTrue(pool.ActiveTraces().All(t => t.Actions.Length == 0));
        }
        pool.Apply(batch.BatchId, responses);
        Assert.Throws<InvalidOperationException>(() => pool.Apply(batch.BatchId, responses));
        var next = pool.ReadBatch();
        Assert.Throws<InvalidOperationException>(() => pool.Apply(batch.BatchId, Responses(next)));
    }

    [TestMethod]
    public void BiddingUpdatesUseFrozenNeuralPlayForOpponentSeatsWithoutTrainingOnIt()
    {
        var pool = new RolloutPool(Options("bidding", 1, 1, 10, start: 16));
        bool opponentPlay = false;
        for (int n = 0; n < 60; n++)
        {
            var batch = pool.ReadBatch();
            foreach (var decision in batch.Requests)
            {
                Assert.AreEqual(decision.Model == "bidding" && decision.Seat == 0, decision.Learner);
                if (decision.Model == "playing" && decision.Seat != 0)
                {
                    opponentPlay = true;
                    Assert.AreEqual("play-v1", decision.PolicyVersion);
                }
            }
            if (batch.Done)
            {
                Assert.IsTrue(opponentPlay);
                Assert.AreEqual(1, batch.CompletedGames);
                Assert.IsTrue(batch.LearnerDecisions is 1 or 2);
                return;
            }
            if (batch.Requests.Length > 0)
                pool.Apply(batch.BatchId, Responses(batch, r => r.Phase == (int)GameState.Bidding1
                    ? GameAction.Interest(true) : new HeuristicPolicy(BiddingStyle.Conservative).ChooseAction(r.Observation)));
        }
        Assert.Fail("Bidding rollout did not terminate");
    }

    [TestMethod]
    public void CompleteMixedTablePlayingRolloutsReplayWithConservedRewards()
    {
        var pool = new RolloutPool(Options());
        int selected = 0, completed = 0;
        bool frozenBidding = false;
        for (int n = 0; n < 2000; n++)
        {
            var batch = pool.ReadBatch();
            foreach (var terminal in batch.Completed)
            {
                completed++;
                Assert.AreEqual(0, terminal.Trace.Rewards.Sum());
                Assert.IsTrue(EpisodeRunner.Replay(terminal.Trace).Terminal);
            }
            foreach (var request in batch.Requests)
            {
                if (request.Learner) { selected++; Assert.AreEqual("playing", request.Model); }
                else if (request.Model == "bidding") frozenBidding = true;
            }
            if (batch.Done)
            {
                Assert.AreEqual(selected, batch.LearnerDecisions);
                Assert.IsGreaterThanOrEqualTo(200, selected);
                Assert.AreEqual(completed, batch.LaunchedGames);
                Assert.IsTrue(frozenBidding);
                return;
            }
            if (batch.Requests.Length > 0) pool.Apply(batch.BatchId, Responses(batch));
        }
        Assert.Fail("Playing rollout did not terminate");
    }

    [TestMethod]
    public void AllPassPlayingCollectionFailsAtGameCapInsteadOfHanging()
    {
        var pool = new RolloutPool(Options("playing", 1, 2, 2));
        for (int n = 0; n < 4; n++)
        {
            var batch = pool.ReadBatch();
            pool.Apply(batch.BatchId, Responses(batch, _ => GameAction.Interest(false)));
        }
        var final = pool.ReadBatch();
        Assert.HasCount(2, final.Completed);
        Assert.IsFalse(final.Done);
        Assert.IsTrue(final.Completed.All(t => EpisodeRunner.Replay(t.Trace).Terminal));
        Assert.Throws<InvalidOperationException>(() => pool.ReadBatch());
    }
}
