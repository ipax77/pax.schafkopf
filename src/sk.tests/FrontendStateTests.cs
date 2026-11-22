using sk.shared;
using sk.weblib.Presentation;

namespace sk.tests;

[TestClass]
public sealed class FrontendStateTests
{
    [TestMethod]
    public void RoundNumbersCountPassedDealsAndRestartWithTheSession()
    {
        var game = new Game();
        Assert.AreEqual(0, game.ToPublicGameState(0).RoundNumber);
        game.ResetEpisode(42);
        Assert.AreEqual(1, game.ToPublicGameState(0).RoundNumber);
        for (int round = 1; round <= 3; round++)
        {
            for (int i = 0; i < 4; i++) game.Step(game.ActivePlayer, GameAction.Interest(false));
            var result = game.ToPublicGameState(0);
            Assert.AreEqual(round, result.RoundNumber);
            Assert.AreEqual(round, result.PublicGameResult!.RoundNumber);
            Assert.AreEqual(game.Revision, result.Revision);
            // A seat replacement cannot renumber the already completed deal.
            game.Table.Players[1].Player = new() { Name = "Replacement", Guid = Guid.NewGuid() };
            Assert.AreEqual(round, game.ToPublicGameState(1).PublicGameResult!.RoundNumber);
            game.Reset();
            Assert.AreEqual(round + 1, game.ToPublicGameState(0).RoundNumber);
        }
        game.ResetEpisode(43);
        Assert.AreEqual(1, game.ToPublicGameState(0).RoundNumber);
    }

    [TestMethod]
    public void PlayedResultKeepsItsRoundAndOriginalPlayers()
    {
        var game = new Game();
        for (int seat = 0; seat < 4; seat++)
            game.Table.Players[seat].Player = new() { Name = $"Player {seat}", Guid = Guid.NewGuid() };
        game.ResetEpisode(42);
        for (int i = 0; i < 4; i++) game.Step(game.ActivePlayer, GameAction.Interest(i == 0));
        game.Step(game.ActivePlayer, GameAction.Bid(GameType.Wenz));
        while (game.GameState == GameState.Playing) game.Step(game.ActivePlayer, game.GetLegalActions()[0]);
        var finished = game.ToPublicGameState(0);
        Assert.AreEqual(1, finished.PublicGameResult!.RoundNumber);
        Assert.AreEqual(8, finished.Turn);
        game.Table.Players[1].Player = new() { Name = "New guest", Guid = Guid.NewGuid() };
        Assert.AreEqual("Player 1", game.ToPublicGameState(0).PublicGameResult!.Players[1].Name);
        game.Reset();
        Assert.AreEqual(2, game.ToPublicGameState(0).RoundNumber);
        Assert.AreEqual(1, finished.PublicGameResult.RoundNumber);
    }

    [TestMethod]
    public void PlaybackDeduplicatesAndFreezesWinnerBeforeNextTrick()
    {
        var playback = new TrickPlayback();
        var state = State();
        playback.Sync(state);
        var cards = Enumerable.Range(0, 4).Select(GameAction.FromCardId).ToArray();
        state.Table.CurrentTrick[2] = cards[2];
        state.LeadingPlayer = 2;
        state.Revision++;
        var first = playback.Observe(state);
        Assert.HasCount(1, first);
        Assert.AreEqual(2, first[0].Seat);
        Assert.IsEmpty(playback.Observe(state));

        state.Table.PreviouseTrick = cards;
        state.Table.Players[1].Tricks = [cards[2], cards[3], cards[0], cards[1]];
        state.Table.CurrentTrick = [null, GameAction.FromCardId(10), null, null];
        state.Turn = 1;
        state.LeadingPlayer = 1;
        state.ActivePlayer = 2; // The winning player has already led the next trick.
        state.Revision += 4;
        var steps = playback.Observe(state);
        CollectionAssert.AreEqual(new[] { 3, 0, 1 }, steps.Take(3).Select(s => s.Seat).ToArray());
        Assert.AreEqual(1, steps[3].Winner);
        Assert.AreEqual(cards.Sum(c => c.GetValue()), steps[3].Points);
        Assert.AreEqual(GameAction.FromCardId(10), steps[4].Card);
        state.ActivePlayer = 3;
        Assert.AreEqual(1, steps[3].Winner);
        Assert.IsEmpty(playback.Observe(state));
    }

    [TestMethod]
    public void ReconnectSkipsHistoryAndRoundResetAllowsTheSameCardAgain()
    {
        var playback = new TrickPlayback();
        var state = State();
        var card = GameAction.FromCardId(0);
        state.Table.CurrentTrick[0] = card;
        playback.Sync(state);
        Assert.IsEmpty(playback.Observe(state));
        var stale = State();
        stale.Revision = 0;
        stale.Table.CurrentTrick[1] = GameAction.FromCardId(1);
        Assert.IsEmpty(playback.Observe(stale));
        state.RoundNumber = 2;
        state.Table.CurrentTrick = new Card?[4];
        Assert.IsTrue(playback.NeedsSync(state));
        playback.Sync(state);
        state.Revision++;
        state.Table.CurrentTrick[0] = card;
        Assert.HasCount(1, playback.Observe(state));
        state.Turn = 3;
        Assert.IsTrue(playback.NeedsSync(state));
    }

    [TestMethod]
    public void FinalTrickProducesOneCollectionAndNoReplayOnReadinessUpdates()
    {
        var playback = new TrickPlayback();
        var state = State();
        state.Turn = 7;
        playback.Sync(state);
        var cards = Enumerable.Range(20, 4).Select(GameAction.FromCardId).ToArray();
        state.Table.PreviouseTrick = cards;
        state.Table.Players[3].Tricks = [cards[0], cards[1], cards[2], cards[3]];
        state.Turn = 8;
        state.GameState = GameState.Finished;
        state.Revision++;
        var steps = playback.Observe(state);
        Assert.HasCount(5, steps);
        Assert.AreEqual(3, steps[^1].Winner);
        state.Table.Players[3].ReadyForNextRound = true;
        state.Revision++;
        Assert.IsEmpty(playback.Observe(state));
    }

    [TestMethod]
    public void TrickLayersFollowPlayOrderAcrossReconnectAndNextTrick()
    {
        var state = State();
        var cards = Enumerable.Range(0, 4).Select(GameAction.FromCardId).ToArray();
        state.LeadingPlayer = 2;
        state.Table.CurrentTrick = cards;
        CollectionAssert.AreEqual(new[] { 2, 3, 0, 1 }, TrickPlayback.CurrentPlayOrder(state));
        state.Table.PreviouseTrick = cards;
        state.Table.Players[3].Tricks = [cards[2], cards[3], cards[0], cards[1]];
        state.LeadingPlayer = 3; // The next trick must not reorder the previous one.
        state.Table.CurrentTrick = [null, null, null, GameAction.FromCardId(10)];
        CollectionAssert.AreEqual(new[] { 3 }, TrickPlayback.CurrentPlayOrder(state));
        CollectionAssert.AreEqual(new[] { 2, 3, 0, 1 }, TrickPlayback.PreviousPlayOrder(state));
    }

    private static PublicGameState State() => new()
    {
        RoundNumber = 1, Revision = 10, GameState = GameState.Playing,
        YourPosition = 0, Table = new() { Guid = Guid.Empty }
    };
}
