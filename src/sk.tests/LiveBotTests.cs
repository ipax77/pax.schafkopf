using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using sk.api.Services;
using sk.bot;
using sk.shared;
using System.Collections.Concurrent;

namespace sk.tests;

public partial class GameHubServiceTests
{
    [TestMethod]
    public async Task ThreeNativeBotsCompleteRepeatedHumanTableRounds()
    {
        using var fixture = new NeuralPolicyTests.Fixture();
        var (id, humans) = BotTable();
        using var scheduler = Scheduler(new NeuralPolicy(fixture.Manifest));
        await scheduler.StartAsync(CancellationToken.None);
        try
        {
            for (int round = 0; round < 3; round++)
            {
                await Until(() =>
                {
                    var observation = _gameHubService.GetBotTurn(id, humans[0].Guid).Observation;
                    if (observation.Phase == GameState.Finished) return true;
                    if (observation.Seat == observation.ActivePlayer) HumanAction(id, humans[0], observation);
                    return false;
                });
                var state = await Snapshot(id);
                Assert.HasCount(3, state.Table.Players.Where(p => p.IsBot));
                Assert.AreEqual(8, state.Turn);
                Assert.AreEqual(0, state.Table.Players.Sum(p => p.Cash));
                Assert.IsTrue(_gameHubService.Ready(id, humans[0].Guid));
            }
        }
        finally { await scheduler.StopAsync(CancellationToken.None); }
    }

    private BotTurnScheduler Scheduler(IGamePolicy? policy = null, TimeSpan? delay = null) => new(
        _gameHubService, policy ?? new HeuristicPolicy(BiddingStyle.Conservative),
        Options.Create(new BotTurnOptions
        {
            TurnDelay = delay ?? TimeSpan.Zero, TrickDelay = delay ?? TimeSpan.Zero,
            PollInterval = TimeSpan.FromMilliseconds(1)
        }), NullLogger<BotTurnScheduler>.Instance);

    private (Guid Id, Player[] Humans) BotTable(int botCount = 3)
    {
        var humans = Enumerable.Range(0, 4 - botCount).Select(i => new Player
            { Guid = Guid.NewGuid(), Name = $"Human {i}", ConnectionId = $"human-{Guid.NewGuid()}" }).ToArray();
        var id = _gameHubService.CreateNewGame(humans[0]);
        foreach (var human in humans.Skip(1)) _gameHubService.JoinGame(id, human);
        _gameHubService.FillBotSeats(id, humans[0].Guid);
        return (id, humans);
    }

    private async Task<PublicGameState> Snapshot(Guid id)
    {
        PublicGameState? result = null;
        _mockSingleClientProxy.Setup(p => p.SendCoreAsync("ReceiveGameState", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((_, args, _) => result = (PublicGameState)args[0]!)
            .Returns(Task.CompletedTask);
        await _gameHubService.BroadcastGame(id);
        return result ?? throw new AssertFailedException("No human received the state");
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!condition()) await Task.Delay(5, timeout.Token);
    }

    private void HumanAction(Guid id, Player player, GameObservation observation)
    {
        var action = observation.Phase switch
        {
            GameState.Bidding1 => GameAction.Interest(true),
            GameState.Bidding2 => observation.LegalActions.First(a => a.Kind == ActionKind.Bid && !a.Tout),
            _ => new HeuristicPolicy(BiddingStyle.Conservative).ChooseAction(observation)
        };
        switch (action.Kind)
        {
            case ActionKind.Interest:
                _gameHubService.SubmitBidding1(id, player.Guid, new() { WouldPlay = action.WouldPlay }); break;
            case ActionKind.Bid:
                _gameHubService.SubmitBidding2(id, player.Guid, new() { ProposedGame = action.GameType, ProposedSuit = action.Suit }); break;
            case ActionKind.Play:
                _gameHubService.PlayCard(id, player.Guid, GameAction.FromCardId(action.CardId)); break;
            default: Assert.Fail("Unexpected human action"); break;
        }
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public async Task LiveSchedulerCompletesFourPlayedRoundsWithOneToThreeBots(int botCount)
    {
        var (id, humans) = BotTable(botCount);
        var observations = new ConcurrentQueue<GameObservation>();
        var policy = new CallbackPolicy(o =>
        {
            observations.Enqueue(o);
            return new HeuristicPolicy(BiddingStyle.Conservative).ChooseAction(o);
        });
        using var scheduler = Scheduler(policy);
        await scheduler.StartAsync(CancellationToken.None);
        try
        {
            for (int round = 0; round < 4; round++)
            {
                await Until(() =>
                {
                    foreach (var human in humans)
                    {
                        var observation = _gameHubService.GetBotTurn(id, human.Guid).Observation;
                        if (observation.Phase == GameState.Finished) return true;
                        if (observation.Seat == observation.ActivePlayer) HumanAction(id, human, observation);
                    }
                    return false;
                });
                var state = await Snapshot(id);
                Assert.AreEqual(8, state.Turn);
                Assert.AreEqual(0, state.Table.Players.Sum(p => p.Cash));
                Assert.HasCount(botCount, state.Table.Players.Where(p => p.IsBot));
                Assert.IsTrue(state.Table.Players.Where(p => p.IsBot).All(p =>
                    p.ReadyForNextRound && !p.IsConnected && p.Player.ConnectionId == null && p.Player.Name.StartsWith("Bot ")));
                Assert.IsNull(_gameHubService.GetActiveBotTurn(id));
                for (int i = 0; i < humans.Length; i++)
                    Assert.AreEqual(i == humans.Length - 1, _gameHubService.Ready(id, humans[i].Guid));
            }
            Assert.IsTrue(observations.Any(o => o.Phase == GameState.Playing));
            Assert.IsTrue(observations.Any(o => o.Phase == GameState.Bidding1 && o.History.Count == 0), "Bot-led auction after dealer rotation");
        }
        finally { await scheduler.StopAsync(CancellationToken.None); }
    }

    [TestMethod]
    public async Task AllPassReadiesBotsAndWaitsForHumanAcrossRounds()
    {
        var (id, humans) = BotTable();
        using var scheduler = Scheduler(new CallbackPolicy(_ => GameAction.Interest(false)));
        await scheduler.StartAsync(CancellationToken.None);
        try
        {
            for (int round = 0; round < 4; round++)
            {
                await Until(() =>
                {
                    var observation = _gameHubService.GetBotTurn(id, humans[0].Guid).Observation;
                    if (observation.Phase == GameState.Finished) return true;
                    if (observation.Seat == observation.ActivePlayer) _gameHubService.SubmitBidding1(id, humans[0].Guid, new());
                    return false;
                });
                var state = await Snapshot(id);
                Assert.AreEqual(GameType.None, state.PublicGameResult!.GameResult.GameType);
                Assert.IsTrue(state.Table.Players.Where(p => p.IsBot).All(p => p.ReadyForNextRound));
                Assert.IsFalse(state.Table.Players[0].ReadyForNextRound);
                Assert.IsNull(_gameHubService.GetActiveBotTurn(id));
                Assert.IsTrue(_gameHubService.Ready(id, humans[0].Guid));
            }
        }
        finally { await scheduler.StopAsync(CancellationToken.None); }
    }

    [TestMethod]
    public async Task BotSeatsArePrivateServerOwnedAndReplaceableOnlyBetweenRounds()
    {
        var (id, humans) = BotTable();
        var state = await Snapshot(id);
        var bot = state.Table.Players[1];
        Assert.IsTrue(state.Table.Players.Skip(1).All(p => p.Hand.All(c => c.Suit == Suit.None) && p.StartingHand.Count == 0));
        Assert.Throws<InvalidOperationException>(() => _gameHubService.RejoinGame(id, bot.Player));
        Assert.Throws<InvalidOperationException>(() => _gameHubService.AttachConnection(id, bot.Player.Guid, "fake"));
        Assert.Throws<InvalidOperationException>(() => _gameHubService.SubmitBidding1(id, bot.Player.Guid, new()));
        Assert.Throws<InvalidOperationException>(() => _gameHubService.FillBotSeats(id, Guid.NewGuid()));
        Assert.Throws<InvalidOperationException>(() => _gameHubService.RemoveBot(id, humans[0].Guid, 1));
        _gameHubService.SubmitBidding1(id, humans[0].Guid, new());
        while (_gameHubService.GetActiveBotTurn(id) is { } turn)
            Assert.IsTrue(_gameHubService.TryApplyBotAction(id, turn.PlayerId, turn.Turn.Revision, GameAction.Interest(false)));
        _gameHubService.RemoveBot(id, humans[0].Guid, 1);
        var afterRemoval = await Snapshot(id);
        Assert.AreEqual(bot.Player.Guid, afterRemoval.PublicGameResult!.Players[1].Guid);
        Assert.AreEqual(0, afterRemoval.PublicGameResult.PlayerChanges.Sum());
        Assert.IsFalse(_gameHubService.TryApplyBotAction(id, bot.Player.Guid, 0, GameAction.Interest(false)));
        Assert.IsFalse(_gameHubService.Ready(id, humans[0].Guid), "Empty seats must not count as ready");
        var newcomer = new Player { Guid = Guid.NewGuid(), Name = "New human", ConnectionId = "new" };
        _gameHubService.JoinGame(id, newcomer);
        Assert.IsFalse(_gameHubService.Ready(id, newcomer.Guid), "Lineup changes clear previous human readiness");
        Assert.IsTrue(_gameHubService.Ready(id, humans[0].Guid));
        Assert.HasCount(8, _gameHubService.GetBotTurn(id, newcomer.Guid).Observation.Hand);
    }

    [TestMethod]
    public async Task DisconnectAndRejoinInvalidateInFlightDecisionWithoutDuplicateScheduling()
    {
        var (id, humans) = BotTable();
        _gameHubService.SubmitBidding1(id, humans[0].Guid, new());
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int calls = 0;
        using var scheduler = Scheduler(new CallbackPolicy(_ =>
        {
            Interlocked.Increment(ref calls);
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return GameAction.Interest(false);
        }));
        await scheduler.StartAsync(CancellationToken.None);
        try
        {
            await Until(() => entered.IsSet);
            var old = _gameHubService.GetActiveBotTurn(id)!;
            _gameHubService.DisconnectPlayer(id, humans[0].Guid, humans[0].ConnectionId);
            Assert.IsNull(_gameHubService.GetActiveBotTurn(id));
            humans[0].ConnectionId = "rejoined";
            _gameHubService.RejoinGame(id, humans[0]);
            _gameHubService.DisconnectPlayer(id, humans[0].Guid, "old-connection");
            Assert.IsNotNull(_gameHubService.GetActiveBotTurn(id));
            Assert.IsFalse(_gameHubService.TryApplyBotAction(id, old.PlayerId, old.Turn.Revision, GameAction.Interest(false)));
            await Task.Delay(30);
            Assert.AreEqual(1, calls, "Only one policy call is allowed while this table's decision is in flight");
            _gameHubService.DisconnectPlayer(id, humans[0].Guid);
            release.Set();
            await Task.Delay(30);
            Assert.HasCount(1, _gameHubService.GetBotTurn(id, humans[0].Guid).Observation.History);
            _gameHubService.RejoinGame(id, humans[0]);
            await Until(() => _gameHubService.GetBotTurn(id, humans[0].Guid).Observation.Phase == GameState.Finished);
            Assert.HasCount(4, _gameHubService.GetBotTurn(id, humans[0].Guid).Observation.History);
        }
        finally { release.Set(); await scheduler.StopAsync(CancellationToken.None); }
    }

    [TestMethod]
    public async Task CancellationAndCleanupDiscardPendingWorkAndReleaseJoinCode()
    {
        var (id, humans) = BotTable();
        var state = await Snapshot(id);
        _gameHubService.SubmitBidding1(id, humans[0].Guid, new());
        var turn = _gameHubService.GetActiveBotTurn(id)!;
        using var scheduler = Scheduler(delay: TimeSpan.FromSeconds(10));
        await scheduler.StartAsync(CancellationToken.None);
        await scheduler.StopAsync(CancellationToken.None);
        Assert.AreEqual(turn.Turn.Revision, _gameHubService.GetActiveBotTurn(id)!.Turn.Revision);
        _gameHubService.DisconnectPlayer(id, humans[0].Guid);
        _gameHubService.CleanupAbandonedTables(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(30));
        Assert.Contains(id, _gameHubService.GameIds);
        _gameHubService.CleanupAbandonedTables(DateTimeOffset.UtcNow.AddHours(1), TimeSpan.FromMinutes(30));
        Assert.DoesNotContain(id, _gameHubService.GameIds);
        Assert.IsFalse(_gameHubService.TryApplyBotAction(id, turn.PlayerId, turn.Turn.Revision, GameAction.Interest(false)));
        Assert.Throws<InvalidOperationException>(() => _gameHubService.JoinGame(state.ShortCode, humans[0]));
        await _gameHubService.BroadcastGame(id);
        var second = BotTable();
        _gameHubService.LeaveGame(second.Id, second.Humans[0].Guid);
        Assert.DoesNotContain(second.Id, _gameHubService.GameIds);
    }

    [TestMethod]
    public async Task BotDeclarationAndSettlementPreserveResultsWhenDeclarerIsReplaced()
    {
        var (id, humans) = BotTable();
        var conservative = new HeuristicPolicy(BiddingStyle.Conservative);
        using var scheduler = Scheduler(new CallbackPolicy(o => o.Phase == GameState.Bidding1
            ? GameAction.Interest(true) : conservative.ChooseAction(o)));
        await scheduler.StartAsync(CancellationToken.None);
        try
        {
            _gameHubService.SubmitBidding1(id, humans[0].Guid, new());
            await Until(() =>
            {
                var observation = _gameHubService.GetBotTurn(id, humans[0].Guid).Observation;
                if (observation.Phase == GameState.Finished) return true;
                if (observation.Seat == observation.ActivePlayer) HumanAction(id, humans[0], observation);
                return false;
            });
            var before = await Snapshot(id);
            var declarer = before.Bidding2Result!.PlayerIndex;
            Assert.IsTrue(before.Table.Players[declarer].IsBot);
            _gameHubService.RemoveBot(id, humans[0].Guid, declarer);
            _gameHubService.FillBotSeats(id, humans[0].Guid);
            var after = await Snapshot(id);
            Assert.AreNotEqual(before.Table.Players[declarer].Player.Guid, after.Table.Players[declarer].Player.Guid);
            Assert.AreEqual(before.PublicGameResult!.Players[declarer].Guid, after.PublicGameResult!.Players[declarer].Guid);
            CollectionAssert.AreEqual(before.PublicGameResult.PlayerChanges, after.PublicGameResult.PlayerChanges);
            CollectionAssert.AreEqual(before.PublicGameResult.StartingHands[declarer], after.PublicGameResult.StartingHands[declarer]);
        }
        finally { await scheduler.StopAsync(CancellationToken.None); }
    }

    [TestMethod]
    public async Task BlockedPolicyAtOneTableDoesNotHoldTableLockOrStopOtherTables()
    {
        var first = BotTable();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int calls = 0;
        using var scheduler = Scheduler(new CallbackPolicy(_ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            }
            return GameAction.Interest(false);
        }));
        await scheduler.StartAsync(CancellationToken.None);
        try
        {
            _gameHubService.SubmitBidding1(first.Id, first.Humans[0].Guid, new());
            await Until(() => entered.IsSet);
            var second = BotTable();
            _gameHubService.SubmitBidding1(second.Id, second.Humans[0].Guid, new());
            await Until(() => _gameHubService.GetBotTurn(second.Id, second.Humans[0].Guid).Observation.Phase == GameState.Finished);
            _gameHubService.LeaveGame(first.Id, first.Humans[0].Guid);
            release.Set();
            Assert.DoesNotContain(first.Id, _gameHubService.GameIds);
        }
        finally { release.Set(); await scheduler.StopAsync(CancellationToken.None); }
    }

    [TestMethod]
    public async Task TenTablesProgressIndependently()
    {
        var tables = Enumerable.Range(0, 10).Select(_ => BotTable()).ToArray();
        using var scheduler = Scheduler(new CallbackPolicy(_ => GameAction.Interest(false)));
        await scheduler.StartAsync(CancellationToken.None);
        try
        {
            foreach (var table in tables) _gameHubService.SubmitBidding1(table.Id, table.Humans[0].Guid, new());
            await Until(() => tables.All(t => _gameHubService.GetBotTurn(t.Id, t.Humans[0].Guid).Observation.Phase == GameState.Finished));
        }
        finally { await scheduler.StopAsync(CancellationToken.None); }
    }

    private sealed class CallbackPolicy(Func<GameObservation, GameAction> choose) : IGamePolicy
    {
        public GameAction ChooseAction(GameObservation observation) => choose(observation);
    }
}
