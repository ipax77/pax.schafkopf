using sk.bot;
using sk.shared;
using sk.training;

namespace sk.tests;

[TestClass]
public sealed class GameplayPolicyTests
{
    private static Card C(Suit suit, Rank rank) => new() { Suit = suit, Rank = rank };
    private static GameObservation Observe(Card[] hand)
    {
        var game = new HeadlessGame(); game.Reset(42);
        return game.Observe(0) with { Hand = hand.Select(GameAction.ToCardId).ToArray() };
    }

    [TestMethod]
    public void RufUsesTrumpCountAndSideSuitCoverageRegardlessOfTrumpQuality()
    {
        var hand = new[] { C(Suit.Schellen, Rank.Unter), C(Suit.Herz, Rank.Seven), C(Suit.Herz, Rank.Eight),
            C(Suit.Herz, Rank.Nine), C(Suit.Herz, Rank.King), C(Suit.Gras, Rank.Seven), C(Suit.Eichel, Rank.Seven), C(Suit.Eichel, Rank.Eight) };
        var policy = new GameplayPolicy();
        Assert.IsTrue(policy.AcceptsContract(Observe(hand), GameAction.Bid(GameType.Ruf, Suit.Gras)));
        hand[0] = C(Suit.Schellen, Rank.Seven);
        Assert.IsFalse(policy.AcceptsContract(Observe(hand), GameAction.Bid(GameType.Ruf, Suit.Gras)));
    }

    [TestMethod]
    public void WenzUsesThreeAcesOrTwoUntersWithAceHeadedFourCardSuit()
    {
        var policy = new GameplayPolicy();
        var hand = new[] { C(Suit.Gras, Rank.Ace), C(Suit.Herz, Rank.Ace), C(Suit.Eichel, Rank.Ace), C(Suit.Gras, Rank.Ten),
            C(Suit.Gras, Rank.King), C(Suit.Gras, Rank.Ober), C(Suit.Schellen, Rank.Seven), C(Suit.Schellen, Rank.Eight) };
        var bid = GameAction.Bid(GameType.Wenz);
        Assert.IsTrue(policy.AcceptsContract(Observe(hand), bid)); // Forehand, even without Unter.
        Assert.IsFalse(policy.AcceptsContract(Observe(hand) with { Dealer = 0 }, bid));
        hand[1] = C(Suit.Herz, Rank.Unter); hand[2] = C(Suit.Eichel, Rank.Unter);
        Assert.IsTrue(policy.AcceptsContract(Observe(hand) with { Dealer = 0 }, bid));
        hand[0] = C(Suit.Gras, Rank.Seven);
        Assert.IsFalse(policy.AcceptsContract(Observe(hand), bid));
    }

    internal static GameObservation PartnerTrick(bool partnerWinning = true)
    {
        var hand = new[] { C(Suit.Schellen, Rank.King), C(Suit.Eichel, Rank.Unter) };
        return Observe(hand) with
        {
            Seat = 3, ActivePlayer = 3, Phase = GameState.Playing, LeadingPlayer = 0,
            Contract = new(0, GameType.Wenz, Suit.None, false),
            CurrentTrick = [GameAction.ToCardId(C(Suit.Gras, Rank.King)),
                GameAction.ToCardId(C(Suit.Gras, partnerWinning ? Rank.Ace : Rank.Nine)),
                GameAction.ToCardId(C(Suit.Gras, Rank.Eight)), -1],
            LegalActions = hand.Select(GameAction.Play).ToArray()
        };
    }

    [TestMethod]
    public void WholeTrickAndKnownPartnershipChangeDecisionWithIdenticalHandAndLead()
    {
        var policy = new GameplayPolicy();
        var partner = PartnerTrick();
        var opponent = PartnerTrick(false);
        Assert.AreEqual(GameAction.Play(C(Suit.Schellen, Rank.King)), policy.ChooseAction(partner));
        Assert.AreEqual(GameAction.Play(C(Suit.Eichel, Rank.Unter)), policy.ChooseAction(opponent));
        Assert.AreEqual("preserve-secure-partner-winner", GameplayPolicy.PlayAdvice(partner)?.Reason);
        Assert.IsNull(GameplayPolicy.PlayAdvice(opponent));
        var block = ObservationEncoder.Blocks.Single(b => b.Name == "current_trick");
        var a = ObservationEncoder.Encode(partner); var b = ObservationEncoder.Encode(opponent);
        Assert.IsFalse(a[block.Offset..(block.Offset + block.Length)].SequenceEqual(b[block.Offset..(block.Offset + block.Length)]));
        // Ruf defenders cannot silently infer which other seat holds the called ace.
        var unknown = partner with { Contract = new(0, GameType.Ruf, Suit.Schellen, false), PublicTeammate = -1 };
        Assert.IsNull(GameplayPolicy.PlayAdvice(unknown));
    }

    [TestMethod]
    public void DiagnosticsRecordActionablePartnerOvertakeWithoutChangingLegalActions()
    {
        var observation = PartnerTrick();
        var diagnostics = new GameplayDiagnostics();
        diagnostics.Record(observation, GameAction.Play(C(Suit.Eichel, Rank.Unter)));
        diagnostics.Record(observation, GameAction.Play(C(Suit.Schellen, Rank.King)));
        var stats = diagnostics.Stats["preserve-secure-partner-winner"];
        Assert.AreEqual(2, stats.Opportunities); Assert.AreEqual(1, stats.Followed);
        Assert.AreEqual(1, stats.Overtakes);
        Assert.HasCount(1, stats.Examples); Assert.HasCount(2, observation.LegalActions);
    }

    [TestMethod]
    public void LeadsDrawTrumpsSearchCalledAceAndPreferUnplayedSuits()
    {
        var hand = new[] { C(Suit.Herz, Rank.Seven), C(Suit.Eichel, Rank.Ace), C(Suit.Gras, Rank.Seven) };
        var opening = Observe(hand) with
        {
            Phase = GameState.Playing, Seat = 1, ActivePlayer = 1, LeadingPlayer = 1,
            Contract = new(0, GameType.Ruf, Suit.Gras, false), CurrentTrick = [-1, -1, -1, -1],
            LegalActions = hand.Select(GameAction.Play).ToArray(), History = []
        };
        var teacher = new GameplayPolicy();
        Assert.AreEqual(GameAction.Play(C(Suit.Gras, Rank.Seven)), teacher.ChooseAction(opening));
        var declarer = opening with { Contract = opening.Contract with { Player = 1 } };
        Assert.AreEqual(GameAction.Play(C(Suit.Herz, Rank.Seven)), teacher.ChooseAction(declarer));
        var revealed = opening with { PublicTeammate = 2,
            History = [new(0, GameAction.Play(C(Suit.Gras, Rank.Eight))), new(1, GameAction.Play(C(Suit.Gras, Rank.Nine))),
                new(2, GameAction.Play(C(Suit.Gras, Rank.Ace))), new(3, GameAction.Play(C(Suit.Gras, Rank.King)))] };
        Assert.AreEqual(GameAction.Play(C(Suit.Eichel, Rank.Ace)), teacher.ChooseAction(revealed));
    }

    [TestMethod]
    public void PartnerAdviceDoesNotAssumeTheRemainingOpponentCannotTrump()
    {
        var observation = PartnerTrick() with { CurrentTrick = [GameAction.ToCardId(C(Suit.Gras, Rank.King)),
            GameAction.ToCardId(C(Suit.Gras, Rank.Ace)), -1, -1], Seat = 2, ActivePlayer = 2 };
        Assert.IsNull(GameplayPolicy.PlayAdvice(observation));
    }

    [TestMethod]
    [DataRow("gameplay", "gameplay-v1")]
    [DataRow("conservative", "heuristic-v1/conservative")]
    public void FixedBiddingNeverEntersPpoRequestsAndMixedGamesReplay(string bidder, string version)
    {
        var pool = new RolloutPool(new("playing", 512, 16, 1000, 20261121, 0, "unused-neural-bidder", "player",
            ControlledPercent: 40, Bidder: bidder, Opponents: "mixed", SelfPlayPercent: 25));
        int completed = 0;
        for (int i = 0; i < 2000; i++)
        {
            var batch = pool.ReadBatch();
            foreach (var request in batch.Requests) Assert.AreEqual("playing", request.Model);
            foreach (var terminal in batch.Completed)
            {
                Assert.IsTrue(EpisodeRunner.Replay(terminal.Trace).Terminal);
                Assert.IsTrue(terminal.Trace.PolicyVersions.All(v => v.StartsWith(version + "|")));
                completed++;
            }
            if (batch.Done) { Assert.IsGreaterThan(0, completed); return; }
            if (batch.Requests.Length > 0) pool.Apply(batch.BatchId, batch.Requests.Select(r =>
                new RolloutAction(r.EpisodeId, r.Decision, ObservationEncoder.LocalAction(r.Observation, new GameplayPolicy().ChooseAction(r.Observation)),
                    -Math.Log(r.Legal.Length), 0, r.PolicyVersion)).ToArray());
        }
        Assert.Fail("Fixed-bidder rollout did not finish");
    }

    [TestMethod]
    public void TacticalImitationWeightingOnlyRepeatsTrainingExamples()
    {
        string root = Path.Combine(Path.GetTempPath(), "gameplay-data-" + Guid.NewGuid());
        try
        {
            ImitationData.Generate(Path.Combine(root, "normal"), 128, 16, 20261121, "gameplay-playing");
            ImitationData.Generate(Path.Combine(root, "weighted"), 128, 16, 20261121, "gameplay-playing", 4);
            using var normal = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "normal/dataset.json")));
            using var weighted = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "weighted/dataset.json")));
            int Rows(System.Text.Json.JsonDocument doc, string split) => doc.RootElement.GetProperty("shards").EnumerateArray()
                .Where(s => s.GetProperty("split").GetString() == split).Sum(s => s.GetProperty("rows").GetInt32());
            int extra = weighted.RootElement.GetProperty("tactical_training_rows_added").GetInt32();
            Assert.IsGreaterThan(0, extra);
            Assert.AreEqual(Rows(normal, "training") + extra, Rows(weighted, "training"));
            Assert.AreEqual(Rows(normal, "validation"), Rows(weighted, "validation"));
            CollectionAssert.AreEqual(File.ReadAllBytes(Path.Combine(root, "normal/validation-playing-00000.bin")),
                File.ReadAllBytes(Path.Combine(root, "weighted/validation-playing-00000.bin")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void GameplayTeacherIsLegalPrivateAndReplayable()
    {
        for (ulong seed = 0; seed < 200; seed++)
            Assert.IsTrue(EpisodeRunner.Replay(EpisodeRunner.Play(seed, (int)(seed % 4), seed,
                ["gameplay", "balanced", "gameplay", "conservative"])).Terminal);
        var game = new Game(); game.ResetEpisode(42);
        var before = new GameplayPolicy().ChooseAction(game.Observe(0));
        (game.Table.Players[1].Hand, game.Table.Players[2].Hand) = (game.Table.Players[2].Hand, game.Table.Players[1].Hand);
        Assert.AreEqual(before, new GameplayPolicy().ChooseAction(game.Observe(0)));
    }
}
