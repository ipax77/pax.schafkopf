using sk.bot;
using sk.shared;
using sk.training;

namespace sk.tests;

[TestClass]
public sealed class BotPolicyTests
{
    private static readonly HeuristicPolicy Bot = new();
    private static Card C(Suit suit, Rank rank) => new() { Suit = suit, Rank = rank };
    private static Card[] WeakHand() =>
    [
        C(Suit.Schellen, Rank.Seven), C(Suit.Schellen, Rank.Eight), C(Suit.Schellen, Rank.Nine),
        C(Suit.Gras, Rank.Seven), C(Suit.Gras, Rank.Eight), C(Suit.Gras, Rank.Nine),
        C(Suit.Eichel, Rank.Seven), C(Suit.Eichel, Rank.Eight)
    ];

    private static GameObservation Bid(Card[] hand, bool declaration = false, bool canPass = true)
    {
        var game = new HeadlessGame();
        game.Reset(42);
        return game.Observe(0) with
        {
            Hand = hand.Select(GameAction.ToCardId).ToArray(),
            Phase = declaration ? GameState.Bidding2 : GameState.Bidding1,
            LegalActions = declaration ? GameRules.LegalBids(hand, canPass) : [GameAction.Interest(false), GameAction.Interest(true)]
        };
    }

    private static GameObservation Play(Card[] hand, ContractObservation contract, int seat, int leader,
        int[] trick, int partner = -1, ActionEvent[]? history = null)
    {
        var game = new HeadlessGame();
        game.Reset(42);
        var lead = trick[leader] < 0 ? null : GameAction.FromCardId(trick[leader]);
        return game.Observe(0) with
        {
            Seat = seat, ActivePlayer = seat, LeadingPlayer = leader, Phase = GameState.Playing,
            Hand = hand.Select(GameAction.ToCardId).ToArray(), Contract = contract, CurrentTrick = trick,
            PublicTeammate = partner, History = history ?? [],
            LegalActions = GameRules.LegalCards(hand, new() { GameType = contract.GameType, Suit = contract.Suit,
                Tout = contract.Tout }, lead, false).Select(GameAction.Play).ToArray()
        };
    }

    [TestMethod]
    public void WeakHandDeclinesAndForcedDeclarationRemainsLegal()
    {
        Assert.AreEqual(GameAction.Interest(false), Bot.ChooseAction(Bid(WeakHand())));
        Assert.AreEqual(GameAction.Pass, Bot.ChooseAction(Bid(WeakHand(), true)));
        var forced = Bid(WeakHand(), true, false);
        var action = Bot.ChooseAction(forced);
        Assert.IsTrue(forced.LegalActions.Contains(action));
        Assert.IsFalse(action.Tout);
        Assert.AreEqual(ActionKind.Bid, action.Kind);
    }

    [TestMethod]
    public void SieIsPreferredAndCannotBeOverbid()
    {
        var hand = Enumerable.Range(1, 4).SelectMany(s => new[] { C((Suit)s, Rank.Ober), C((Suit)s, Rank.Unter) }).ToArray();
        Assert.AreEqual(GameAction.Interest(true), Bot.ChooseAction(Bid(hand)));
        Assert.AreEqual(GameAction.Bid(GameType.Sie), Bot.ChooseAction(Bid(hand, true)));
        var behindSie = Bid(WeakHand(), true) with { Contract = new(1, GameType.Sie, Suit.None, false) };
        Assert.AreEqual(GameAction.Pass, Bot.ChooseAction(behindSie));
    }

    [TestMethod]
    public void ToutRequiresUnbeatableHandNotJustManyTrumps()
    {
        var winners = Enumerable.Range(1, 4).SelectMany(s => new[] { C((Suit)s, Rank.Unter), C((Suit)s, Rank.Ace) }).ToArray();
        Assert.AreEqual(GameAction.Bid(GameType.Wenz, tout: true), Bot.ChooseAction(Bid(winners, true)));
        winners[1] = C(Suit.Schellen, Rank.King); // Missing the ace makes Tout unjustified.
        Assert.IsFalse(Bot.ChooseAction(Bid(winners, true)).Tout);
    }

    [TestMethod]
    public void RufPartnerKnowledgeIsPrivateUntilRevealed()
    {
        var ace = C(Suit.Gras, Rank.Ace);
        var holder = Play([ace, C(Suit.Herz, Rank.Seven)], new(0, GameType.Ruf, Suit.Gras, false), 2, 2, [-1, -1, -1, -1]);
        Assert.IsTrue(HeuristicPolicy.AreTeammates(holder, 0));
        Assert.IsFalse(HeuristicPolicy.AreTeammates(holder, 1));
        var defender = holder with { Seat = 1, ActivePlayer = 1, Hand = [0, 1] };
        Assert.IsFalse(HeuristicPolicy.AreTeammates(defender, 2));
        Assert.IsFalse(HeuristicPolicy.AreTeammates(defender, 3));
        Assert.IsTrue(HeuristicPolicy.AreTeammates(defender with { PublicTeammate = 2 }, 3));
        var remembered = holder with { Hand = [0], History = [new(2, GameAction.Play(ace))] };
        Assert.IsTrue(HeuristicPolicy.AreTeammates(remembered, 0));
    }

    [TestMethod]
    public void LastHandFeedsKnownPartnerAndTakesOpponentTrickCheaply()
    {
        var king = C(Suit.Gras, Rank.King);
        var ten = C(Suit.Gras, Rank.Ten);
        int[] trick = [GameAction.ToCardId(C(Suit.Gras, Rank.Seven)),
            GameAction.ToCardId(C(Suit.Gras, Rank.Ace)), GameAction.ToCardId(C(Suit.Gras, Rank.Eight)), -1];
        var observation = Play([king, ten], new(0, GameType.Wenz, Suit.None, false), 3, 0, trick);
        Assert.AreEqual(GameAction.Play(ten), Bot.ChooseAction(observation)); // Seat 1 is a fellow defender.
        trick[1] = GameAction.ToCardId(C(Suit.Gras, Rank.Nine));
        observation = Play([king, ten], new(0, GameType.Wenz, Suit.None, false), 3, 0, trick);
        observation = observation with { Contract = new(1, GameType.Wenz, Suit.None, false) };
        Assert.AreEqual(GameAction.Play(king), Bot.ChooseAction(observation));
    }

    [TestMethod]
    public void CalledAceAndInactiveObservationsRespectEngineContract()
    {
        var ace = C(Suit.Gras, Rank.Ace);
        var observation = Play([ace, C(Suit.Gras, Rank.King)], new(0, GameType.Ruf, Suit.Gras, false), 1, 0,
            [GameAction.ToCardId(C(Suit.Gras, Rank.Seven)), -1, -1, -1]);
        Assert.AreEqual(GameAction.Play(ace), Bot.ChooseAction(observation));
        Assert.Throws<InvalidOperationException>(() => Bot.ChooseAction(observation with { ActivePlayer = 2 }));
    }

    [TestMethod]
    public void BotInputDoesNotDependOnUnseenHandAssignments()
    {
        var game = new Game();
        game.ResetEpisode(42);
        var before = game.Observe(0);
        var beforeAction = Bot.ChooseAction(before);
        (game.Table.Players[1].Hand, game.Table.Players[2].Hand) = (game.Table.Players[2].Hand, game.Table.Players[1].Hand);
        var after = game.Observe(0);
        CollectionAssert.AreEqual(before.Hand.ToArray(), after.Hand.ToArray());
        Assert.AreEqual(beforeAction, Bot.ChooseAction(after));
    }

    [TestMethod]
    public void MixedPoliciesCompleteSeededGamesAndReplayExactly()
    {
        string[] names = ["balanced", "conservative", "random", "balanced"];
        for (ulong seed = 0; seed < 500; seed++)
        {
            var trace = EpisodeRunner.Play(seed, (int)(seed % 4), seed + 123, names);
            Assert.AreEqual(0, trace.Rewards.Sum());
            Assert.IsLessThanOrEqualTo(40, trace.Actions.Length);
            Assert.IsTrue(EpisodeRunner.Replay(trace).Terminal);
        }
    }
}
