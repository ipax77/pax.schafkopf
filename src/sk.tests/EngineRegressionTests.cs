using System.Text.Json;
using sk.shared;

namespace sk.tests;

[TestClass]
public sealed class EngineRegressionTests
{
    [TestMethod]
    public void NoncanonicalActionsAreRejectedAndSharedValuesCannotBeChanged()
    {
        var game = new Game();
        game.ResetEpisode(42);
        RejectedWithoutMutation(game, () => game.Step(0, GameAction.Interest(true) with { CardId = 0 }));
        var continuing = game.Step(0, GameAction.Interest(true));
        Assert.Throws<NotSupportedException>(() => ((IList<int>)continuing.Rewards)[0] = 999);
        for (int p = 1; p < 4; p++) game.Step(p, GameAction.Interest(false));
        foreach (var action in new[]
        {
            GameAction.Pass with { Tout = true }, GameAction.Bid(GameType.None),
            GameAction.Bid(GameType.Sie, tout: true),
            GameAction.Bid(GameType.Wenz) with { WouldPlay = true },
            GameAction.Bid(GameType.Wenz) with { CardId = 5 },
            new GameAction((ActionKind)99)
        }) RejectedWithoutMutation(game, () => game.Step(0, action));
        game.Step(0, GameAction.Bid(GameType.Wenz));
        var legal = game.GetLegalActions()[0];
        RejectedWithoutMutation(game, () => game.Step(0, legal with { Tout = true }));
        RejectedWithoutMutation(game, () => game.Step(0, new(ActionKind.Play, CardId: 32)));
        var card = GameAction.FromCardId(0);
        var changed = card with { Rank = Rank.Ace };
        Assert.AreEqual(Rank.Seven, GameAction.FromCardId(0).Rank);
        Assert.AreEqual(Rank.Ace, changed.Rank);
        Assert.IsTrue(continuing.Rewards.All(r => r == 0));
    }

    [TestMethod]
    public void ActionIdsRoundTripAndOffSuitCardsCompareEqual()
    {
        for (int id = 0; id < GameAction.ActionCount; id++) Assert.AreEqual(id, GameAction.FromId(id).Id);
        Assert.Throws<ArgumentOutOfRangeException>(() => GameAction.FromId(49));
        Assert.Throws<ArgumentOutOfRangeException>(() => GameAction.FromCardId(-1));
        var comparer = new TrickCardComparer(GameType.Wenz, Suit.None, C(Suit.Eichel, Rank.Seven));
        var a = C(Suit.Gras, Rank.Ace);
        var b = C(Suit.Schellen, Rank.Ten);
        Assert.AreEqual(0, comparer.Compare(a, b));
        Assert.AreEqual(0, comparer.Compare(b, a));
        Assert.IsGreaterThan(0, comparer.Compare(C(Suit.Eichel, Rank.Seven), a));
        Assert.IsGreaterThan(0, comparer.Compare(C(Suit.Schellen, Rank.Unter), C(Suit.Eichel, Rank.Ace)));
    }

    private static Card C(Suit s, Rank r) => new() { Suit = s, Rank = r };
    private static string Snapshot(Game g) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(g.ToPublicGameState).ToArray(),
        Observation = g.Observe(0), g.Revision
    });

    private static void RejectedWithoutMutation(Game g, Action action)
    {
        var before = Snapshot(g);
        Assert.Throws<InvalidOperationException>(action);
        Assert.AreEqual(before, Snapshot(g));
    }

    private static Game Auction(params int[] interested)
    {
        var g = new Game();
        g.ResetEpisode(42);
        for (int p = 0; p < 4; p++) g.SetBidding1(p, new() { WouldPlay = interested.Contains(p) });
        return g;
    }

    [TestMethod]
    public void IllegalCardsAndPhasesCannotMutateState()
    {
        var g = new Game();
        g.ResetEpisode(1);
        RejectedWithoutMutation(g, () => g.PlayCard(0, g.Table.Players[0].Hand[0]));
        for (int p = 0; p < 4; p++) g.SetBidding1(p, new() { WouldPlay = p == 0 });
        g.SetBidding2(0, new() { ProposedGame = GameType.Solo, ProposedSuit = Suit.Herz });
        RejectedWithoutMutation(g, () => g.PlayCard(1, g.Table.Players[1].Hand[0]));
        RejectedWithoutMutation(g, () => g.PlayCard(0, g.Table.Players[1].Hand[0]));
        RejectedWithoutMutation(g, () => g.Step(0, GameAction.Interest(true)));
        RejectedWithoutMutation(g, () => g.CreateGameResult());
    }

    [TestMethod]
    public void FollowingSuitIsIdenticalInServerAndPublicRules()
    {
        var g = new Game { GameState = GameState.Playing, ActivePlayer = 1,
            Bidding2Result = new() { GameType = GameType.Solo, Suit = Suit.Herz } };
        var ace = C(Suit.Eichel, Rank.Ace);
        var trump = C(Suit.Herz, Rank.Ace);
        g.Table.CurrentTrick[0] = C(Suit.Eichel, Rank.Seven);
        g.Table.Players[1].Hand = [ace, trump];
        CollectionAssert.AreEqual(new[] { ace }, g.Table.Players[1].GetValidCards(g));
        CollectionAssert.AreEqual(new[] { ace }, g.ToPublicGameState(1).GetValidCards());
        Assert.IsFalse(trump.CanOperate(g.Table.CurrentTrick[0]!, GameType.Solo, Suit.Herz, g.Table.Players[1].Hand));
        RejectedWithoutMutation(g, () => g.PlayCard(1, trump));
    }

    [TestMethod]
    public void CalledAceRulesIncludeFinalTrickAndRunAway()
    {
        var ace = C(Suit.Gras, Rank.Ace);
        var ten = C(Suit.Gras, Rank.Ten);
        var seven = C(Suit.Gras, Rank.Seven);
        var eight = C(Suit.Gras, Rank.Eight);
        var trump = C(Suit.Herz, Rank.Ober);
        var b = new Bidding2Result { GameType = GameType.Ruf, Suit = Suit.Gras };
        CollectionAssert.AreEqual(new[] { ace }, GameRules.LegalCards([ace], b, trump, false));
        CollectionAssert.AreEqual(new[] { trump }, GameRules.LegalCards([ace, trump], b, C(Suit.Eichel, Rank.Seven), false));
        CollectionAssert.AreEqual(new[] { ace }, GameRules.LegalCards([ace, ten, seven], b, null, false));
        Assert.HasCount(4, GameRules.LegalCards([ace, ten, seven, eight], b, null, false));
        CollectionAssert.AreEqual(new[] { ace }, GameRules.LegalCards([ace, ten], b, seven, false));
        Assert.HasCount(2, GameRules.LegalCards([ace, ten], b, seven, true));

        var g = new Game { GameState = GameState.Playing, Bidding2Result = b };
        g.Table.Players[0].Hand = [ace, ten, seven, eight];
        g.PlayCard(0, seven);
        Assert.IsTrue(g.DrunterDurch);
        Assert.AreEqual(0, g.PublicTeammate);
        var h = new Game { GameState = GameState.Playing, Bidding2Result = b };
        h.Table.Players[0].Hand = [ace, ten];
        h.PlayCard(0, ace);
        Assert.IsFalse(h.DrunterDurch);
    }

    [TestMethod]
    public void IllegalBidsDoNotAdvanceOrPoisonAuction()
    {
        var g = Auction(0);
        g.Table.Players[0].Hand = [C(Suit.Eichel, Rank.Ace), C(Suit.Gras, Rank.Seven)];
        foreach (var bid in new BiddingState[]
        {
            new(),
            new() { ProposedGame = GameType.Ruf, ProposedSuit = Suit.Eichel },
            new() { ProposedGame = GameType.Ruf, ProposedSuit = Suit.Herz },
            new() { ProposedGame = GameType.Ruf, ProposedSuit = Suit.Schellen },
            new() { ProposedGame = GameType.Ruf, ProposedSuit = Suit.Gras, Tout = true },
            new() { ProposedGame = GameType.Solo },
            new() { ProposedGame = GameType.Wenz, ProposedSuit = Suit.Gras },
            new() { ProposedGame = GameType.Sie },
            new() { ProposedGame = GameType.Ramsch },
            new() { ProposedGame = (GameType)999 },
            new() { ProposedGame = GameType.Wenz, Sie = true }
        }) RejectedWithoutMutation(g, () => g.SetBidding2(0, bid));
        g.SetBidding2(0, new() { ProposedGame = GameType.Ruf, ProposedSuit = Suit.Gras });
        Assert.AreEqual(GameState.Playing, g.GameState);
    }

    [TestMethod]
    public void RufAllowedInContestedAuctionAndLastPlayerMustDeclare()
    {
        var g = Auction(0, 1);
        g.Table.Players[0].Hand = [C(Suit.Gras, Rank.Seven)];
        Assert.Contains(GameAction.Bid(GameType.Ruf, Suit.Gras), g.GetLegalActions());
        g.SetBidding2(0, new());
        RejectedWithoutMutation(g, () => g.SetBidding2(1, new()));
        g.SetBidding2(1, new() { ProposedGame = GameType.Wenz });
        Assert.AreEqual(1, g.Bidding2Result!.PlayerIndex);
    }

    [TestMethod]
    public void BidPriorityAndSeatPrecedenceAreStable()
    {
        var g = Auction(0, 1, 2, 3);
        var bid = new BiddingState { ProposedGame = GameType.Wenz, Tout = true };
        g.SetBidding2(0, bid);
        bid.ProposedGame = GameType.Sie; // Commands must be copied on acceptance.
        g.SetBidding2(1, new() { ProposedGame = GameType.Solo, ProposedSuit = Suit.Herz });
        g.SetBidding2(2, new() { ProposedGame = GameType.Solo, ProposedSuit = Suit.Eichel, Tout = true });
        g.SetBidding2(3, new() { ProposedGame = GameType.Solo, ProposedSuit = Suit.Gras, Tout = true });
        Assert.AreEqual(2, g.Bidding2Result!.PlayerIndex);
        Assert.AreEqual(Suit.Eichel, g.Bidding2Result.Suit);
        var h = Auction(0, 1);
        h.SetBidding2(0, new() { ProposedGame = GameType.Wenz, Tout = true });
        h.SetBidding2(1, new() { ProposedGame = GameType.Solo, ProposedSuit = Suit.Herz });
        Assert.AreEqual(GameType.Wenz, h.Bidding2Result!.GameType);
    }

    [TestMethod]
    public void SeededDealHasStableGoldenVectorAndResetClearsEpisode()
    {
        var g = new Game();
        g.ResetEpisode(42);
        int[] expected = [2,22,7,3,10,17,9,20,1,26,14,0,16,11,27,5,12,24,4,29,31,25,30,13,8,23,15,6,19,18,28,21];
        CollectionAssert.AreEqual(expected, g.Table.Players.SelectMany(p => p.Hand).Select(GameAction.ToCardId).ToArray());
        var before = JsonSerializer.Serialize(g.Observe(0));
        g.Step(0, GameAction.Interest(true));
        g.Table.CurrentTrick[0] = C(Suit.Gras, Rank.Ace);
        g.Table.PreviouseTrick = [C(Suit.Gras, Rank.Ten), null, null, null];
        g.GameResults.Add(new());
        g.Table.Players[0].Cash = 123;
        g.ResetEpisode(42);
        Assert.AreEqual(before, JsonSerializer.Serialize(g.Observe(0)));
        Assert.IsEmpty(g.GameResults);
        Assert.IsNull(g.Table.PreviouseTrick);
        Assert.IsTrue(g.Table.Players.All(p => p.Cash == 0));
        for (int dealer = 0; dealer < 4; dealer++)
        {
            g.ResetEpisode(42, dealer);
            Assert.AreEqual((dealer + 1) % 4, g.ActivePlayer);
        }
    }

    [TestMethod]
    public void PublicAndPolicyObservationsAreDetachedAndPrivate()
    {
        var g = new Game();
        g.ResetEpisode(42);
        var observation = g.Observe(0);
        var frozen = JsonSerializer.Serialize(observation);
        var view = g.ToPublicGameState(0);
        Assert.HasCount(8, view.Table.Players[1].Hand);
        Assert.IsTrue(view.Table.Players[1].Hand.All(c => c.Suit == Suit.None));
        Assert.IsEmpty(view.Table.Players[1].StartingHand);
        view.Table.Players[0].Hand.Clear();
        view.Table.Players[1].Tricks.Add(C(Suit.Eichel, Rank.Ace));
        view.Table.Players[0].Player.Name = "changed";
        view.Table.CurrentTrick[0] = C(Suit.Eichel, Rank.Ace);
        Assert.HasCount(8, g.Table.Players[0].Hand);
        Assert.IsEmpty(g.Table.Players[1].Tricks);
        Assert.AreEqual("", g.Table.Players[0].Player.Name);
        Assert.IsNull(g.Table.CurrentTrick[0]);
        g.Step(0, GameAction.Interest(false));
        Assert.AreEqual(frozen, JsonSerializer.Serialize(observation));
        Assert.Throws<NotSupportedException>(() => ((IList<int>)observation.Hand)[0] = 31);
    }

    [TestMethod]
    public void ReplayAndRandomGamesPreserveInvariantsAndLegalMasks()
    {
        var rng = new Random(17);
        var engine = new HeadlessGame();
        var live = new Game();
        var contracts = new HashSet<GameType>();
        for (ulong seed = 0; seed < 500; seed++)
        {
            int dealer = (int)(seed % 4);
            engine.Reset(seed, dealer);
            live.ResetEpisode(seed, dealer);
            int steps = 0;
            StepResult? result = null;
            while (!engine.IsTerminal)
            {
                int player = engine.CurrentPlayer;
                var observation = engine.Observe(player);
                Assert.AreEqual(JsonSerializer.Serialize(live.Observe(player)), JsonSerializer.Serialize(observation));
                var legal = engine.LegalActions();
                Assert.IsNotEmpty(legal);
                CollectionAssert.AreEqual(legal.ToArray(), live.ToPublicGameState(player).GetLegalActions().ToArray());
                Assert.AreEqual(legal.Count, observation.LegalActionMask().Count(b => b));
                var action = legal[rng.Next(legal.Count)];
                result = engine.Step(action);
                var other = live.Step(player, action);
                CollectionAssert.AreEqual(result.Rewards.ToArray(), other.Rewards.ToArray());
                Assert.AreEqual(0, result.Rewards.Sum());
                if (!result.Terminal) Assert.IsTrue(result.Rewards.All(r => r == 0));
                var allCards = live.Table.Players.SelectMany(p => p.Hand.Concat(p.Tricks))
                    .Concat(live.Table.CurrentTrick.Where(c => c != null).Select(c => c!)).ToArray();
                Assert.HasCount(32, allCards);
                Assert.AreEqual(32, allCards.Distinct().Count());
                Assert.AreEqual(120, allCards.Sum(c => c.GetValue()));
                Assert.IsLessThanOrEqualTo(40, ++steps);
            }
            if (live.Bidding2Result != null)
            {
                contracts.Add(live.Bidding2Result.GameType);
                Assert.AreEqual(8, live.Turn);
                Assert.AreEqual(32, live.Table.Players.Sum(p => p.Tricks.Count));
                Assert.AreEqual(120, live.Table.Players.Sum(p => p.Tricks.Sum(c => c.GetValue())));
                Assert.IsTrue(live.Table.Players.All(p => p.Hand.Count == 0));
            }
            Assert.HasCount(1, live.GameResults);
            RejectedWithoutMutation(live, () => live.CreateGameResult());
            Assert.IsEmpty(engine.LegalActions());
            Assert.IsEmpty(engine.Observe(0).LegalActions);
        }
        Assert.IsTrue(contracts.IsSupersetOf([GameType.Ruf, GameType.Wenz, GameType.Solo]));
    }

    [TestMethod]
    public void AllPassProducesZeroRewardsAndCanBeResetRepeatedly()
    {
        var engine = new HeadlessGame();
        for (ulong seed = 0; seed < 20; seed++)
        {
            engine.Reset(seed);
            StepResult? result = null;
            for (int i = 0; i < 4; i++) result = engine.Step(GameAction.Interest(false));
            Assert.IsTrue(result!.Terminal);
            CollectionAssert.AreEqual(new int[4], result.Rewards.ToArray());
            Assert.HasCount(4, engine.Observe(0).History);
            Assert.Throws<InvalidOperationException>(() => engine.Step(GameAction.Interest(true)));
        }
    }

    [TestMethod]
    public void SavedActionTraceReplaysAfterResetAndRewardsRemainDetached()
    {
        var engine = new HeadlessGame();
        engine.Reset(137, 1);
        List<int> trace = [];
        StepResult result = new(false, []);
        while (!engine.IsTerminal)
        {
            var actions = engine.LegalActions();
            // Express interest, then select actual bids; play the first legal card.
            var action = actions.FirstOrDefault(a => a.Kind == ActionKind.Interest && a.WouldPlay);
            if (!actions.Contains(action)) action = actions.First(a => a.Kind != ActionKind.Pass);
            trace.Add(action.Id);
            result = engine.Step(action.Id);
        }
        string final = JsonSerializer.Serialize(engine.Observe(0));
        var rewards = result.Rewards.ToArray();
        engine.Reset(999);
        engine.Reset(137, 1);
        foreach (int action in trace) engine.Step(action);
        Assert.AreEqual(final, JsonSerializer.Serialize(engine.Observe(0)));
        CollectionAssert.AreEqual(rewards, result.Rewards.ToArray());
        Assert.Throws<NotSupportedException>(() => ((IList<int>)result.Rewards)[0] = 1000);
    }

    [TestMethod]
    public void SieIsRecognizedWithoutLegacyFlagsAndHasFourfoldTariff()
    {
        var g = Auction(0, 1);
        var sie = Enumerable.Range(0, 32).Select(GameAction.FromCardId).Where(c => c.Rank is Rank.Ober or Rank.Unter).ToList();
        var rest = Enumerable.Range(0, 32).Select(GameAction.FromCardId).Except(sie).ToArray();
        g.Table.Players[0].Hand = [.. sie];
        g.Table.Players[0].StartingHand = [.. sie];
        for (int p = 1; p < 4; p++)
        {
            g.Table.Players[p].Hand = rest.Skip((p - 1) * 8).Take(8).ToList();
            g.Table.Players[p].StartingHand = [.. g.Table.Players[p].Hand];
        }
        g.SetBidding2(0, new() { ProposedGame = GameType.Sie });
        g.SetBidding2(1, new() { ProposedGame = GameType.Solo, ProposedSuit = Suit.Herz, Tout = true });
        while (g.GameState == GameState.Playing) g.Step(g.ActivePlayer, g.GetLegalActions()[0]);
        var result = g.GameResults.Single();
        Assert.AreEqual(GameType.Sie, result.GameType);
        Assert.AreEqual(8, result.PlayerTricks);
        Assert.AreEqual(400, result.Cost);
        Assert.AreEqual(1200, g.Table.Players[0].Cash);
    }
}
