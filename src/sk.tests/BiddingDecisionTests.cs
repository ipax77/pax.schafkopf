using sk.shared;
using sk.weblib.Presentation;

namespace sk.tests;

[TestClass]
public sealed class BiddingDecisionTests
{
    [TestMethod]
    public void SameDecisionPreservesSelectionAcrossRendersAndReconnect()
    {
        var state = State();
        var decision = new BiddingDecision();
        decision.Update(state);
        decision.SelectGameType(GameType.Solo);
        decision.SelectSuit(Suit.Eichel);
        decision.ToggleTout();
        state.Revision++;
        decision.Update(state);
        Assert.IsTrue(decision.CanSubmit);
        state.Table.Players[1].IsConnected = false;
        decision.Update(state);
        Assert.IsFalse(decision.Active);
        Assert.IsFalse(decision.TrySubmit());
        state.Table.Players[1].IsConnected = true;
        decision.Update(state);
        Assert.AreEqual(GameType.Solo, decision.GameType);
        Assert.AreEqual(Suit.Eichel, decision.Suit);
        Assert.IsTrue(decision.Tout);
        Assert.IsTrue(decision.CanSubmit);
    }

    [TestMethod]
    public void NewRoundOrTableResetsSelectionAndSubmission()
    {
        var state = State();
        var decision = new BiddingDecision();
        decision.Update(state);
        decision.SelectGameType(GameType.Wenz);
        Assert.IsTrue(decision.TrySubmit());
        state.RoundNumber++;
        decision.Update(state);
        Assert.IsFalse(decision.Selected);
        Assert.IsFalse(decision.Submitted);
        decision.SelectGameType(GameType.Solo);
        decision.SelectSuit(Suit.Herz);
        state.Table.Guid = Guid.NewGuid();
        decision.Update(state);
        Assert.IsFalse(decision.Selected);
        Assert.AreEqual(Suit.None, decision.Suit);
    }

    [TestMethod]
    public void LegalBidIsRequiredAndGameChangeClearsSuitAndTout()
    {
        var decision = new BiddingDecision();
        decision.Update(State());
        Assert.IsFalse(decision.TrySubmit());
        decision.SelectGameType(GameType.Solo);
        Assert.IsFalse(decision.CanSubmit);
        decision.SelectSuit(Suit.Gras);
        decision.ToggleTout();
        Assert.IsTrue(decision.CanSubmit);
        decision.SelectGameType(GameType.Wenz);
        Assert.AreEqual(Suit.None, decision.Suit);
        Assert.IsFalse(decision.Tout);
        Assert.IsTrue(decision.CanSubmit);
        decision.SelectGameType(GameType.Sie); // This hand cannot bid Sie.
        Assert.AreEqual(GameType.Wenz, decision.GameType);
    }

    [TestMethod]
    public void AuthoritativeUpdatesRemoveAnInvalidSelection()
    {
        var state = State();
        var decision = new BiddingDecision();
        decision.Update(state);
        decision.SelectGameType(GameType.Ruf);
        decision.SelectSuit(Suit.Eichel);
        Assert.IsTrue(decision.CanSubmit);
        state.Table.Players[0].Hand = [new() { Suit = Suit.Eichel, Rank = Rank.Ace }];
        decision.Update(state);
        Assert.IsFalse(decision.Selected);
        Assert.IsFalse(decision.CanSubmit);
    }

    [TestMethod]
    public void SubmissionIsSentOnceAndFailureCanBeRetried()
    {
        var state = State();
        var decision = new BiddingDecision();
        decision.Update(state);
        decision.SelectGameType(GameType.None);
        Assert.IsNull(decision.Result);
        Assert.IsTrue(decision.TrySubmit());
        Assert.IsFalse(decision.TrySubmit());
        var original = decision.Id;
        decision.FailSubmission(original);
        Assert.IsNotNull(decision.Error);
        Assert.IsTrue(decision.TrySubmit());
        state.RoundNumber++;
        decision.Update(state);
        decision.SelectGameType(GameType.Wenz);
        Assert.IsTrue(decision.TrySubmit());
        decision.FailSubmission(original);
        Assert.IsTrue(decision.Submitted);
        Assert.IsNull(decision.Error);
    }

    [TestMethod]
    public void ResyncAllowsRetryIfServerStillExpectsTheDecision()
    {
        var state = State();
        var decision = new BiddingDecision();
        decision.Update(state);
        decision.SelectGameType(GameType.Solo);
        decision.SelectSuit(Suit.Herz);
        decision.ToggleTout();
        Assert.IsTrue(decision.TrySubmit());
        decision.Update(state);
        Assert.IsFalse(decision.TrySubmit());
        decision.Update(state, resync: true);
        Assert.AreEqual(GameType.Solo, decision.GameType);
        Assert.AreEqual(Suit.Herz, decision.Suit);
        Assert.IsTrue(decision.Tout);
        Assert.IsTrue(decision.TrySubmit());
    }

    [TestMethod]
    public void ForcedBidCannotPassAndSieNeedsNoSuit()
    {
        var state = State();
        state.Bidding1Result = new() { InterestedPlayers = [0] };
        var decision = new BiddingDecision();
        decision.Update(state);
        decision.SelectGameType(GameType.None);
        Assert.IsFalse(decision.Selected);
        Assert.IsFalse(decision.TrySubmit());
        state.Table.Players[0].Hand = Enumerable.Range(1, 4)
            .SelectMany(s => new[] { new Card { Suit = (Suit)s, Rank = Rank.Ober },
                new Card { Suit = (Suit)s, Rank = Rank.Unter } }).ToList();
        decision.Update(state);
        decision.SelectGameType(GameType.Sie);
        Assert.IsTrue(decision.CanSubmit);
        Assert.IsTrue(decision.Result!.Sie);
        Assert.AreEqual(Suit.None, decision.Result.Suit);
    }

    [TestMethod]
    public void InterestAndTurnChangesCannotLeaveAnActiveDecision()
    {
        var state = State();
        state.GameState = GameState.Bidding1;
        var decision = new BiddingDecision();
        decision.Update(state);
        Assert.IsTrue(decision.TrySubmit());
        Assert.IsFalse(decision.TrySubmit());
        state.ActivePlayer = 1;
        decision.Update(state);
        Assert.IsFalse(decision.Active);
        Assert.IsFalse(decision.TrySubmit());
        state.ActivePlayer = 0;
        state.GameState = GameState.Playing;
        decision.Update(state);
        Assert.IsFalse(decision.Active);
    }

    private static PublicGameState State()
    {
        var state = new PublicGameState
        {
            RoundNumber = 1, Revision = 1, GameState = GameState.Bidding2,
            YourPosition = 0, ActivePlayer = 0, LeadingPlayer = 0,
            Bidding1Result = new() { InterestedPlayers = [0, 1] }
        };
        foreach (var player in state.Table.Players)
        {
            player.Player = new() { Guid = Guid.NewGuid(), Name = "Player" };
            player.IsConnected = true;
        }
        state.Table.Players[0].Hand = [new() { Suit = Suit.Eichel, Rank = Rank.Seven }];
        return state;
    }
}
