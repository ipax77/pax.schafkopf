using sk.shared;

namespace sk.weblib.Presentation;

/// <summary>One decision, shared by the compact panel and desktop dialog.</summary>
public sealed class BiddingDecision
{
    private PublicGameState state = new();
    public string Id { get; private set; } = "";
    public GameState Phase => state.GameState;
    public bool Active { get; private set; }
    public bool Submitted { get; private set; }
    public bool Also => state.Bidding1Result?.InterestedPlayers.Count > 0;
    public bool Selected { get; private set; }
    public GameType GameType { get; private set; }
    public Suit Suit { get; private set; }
    public bool Tout { get; private set; }
    public string? Error { get; private set; }
    public List<GameType> GameTypes => state.GetValidGameModes();
    public List<Suit> Suits => state.GetValidSuits(GameType);
    public bool CanSubmit => Active && !Submitted && Selected && state.GetLegalActions().Contains(
        GameType == GameType.None ? GameAction.Pass : GameAction.Bid(GameType, Suit, Tout));

    public void Update(PublicGameState next, bool resync = false)
    {
        var id = $"{next.Table.Guid}:{next.RoundNumber}:{next.GameState}:{next.YourPosition}:{next.ActivePlayer}";
        if (id != Id)
        {
            Id = id;
            ClearSelection();
            Submitted = false;
            Error = null;
        }
        state = next;
        Active = next.GameState is GameState.Bidding1 or GameState.Bidding2
            && next.YourPosition is >= 0 and < 4 && next.ActivePlayer == next.YourPosition
            && next.Table.Players.All(p => p.Player.Guid != Guid.Empty && (p.IsBot || p.IsConnected));
        // The server still expects this decision after reconnecting: an earlier
        // send may not have arrived. Retain the choice and allow it to be retried.
        if (resync && Active) Submitted = false;
        // A connection interruption must not discard a partially entered bid.
        if (!Active || !Selected) return;
        if (!GameTypes.Contains(GameType)) ClearSelection();
        else
        {
            if (Suit != Suit.None && !Suits.Contains(Suit)) Suit = Suit.None;
            if (Tout && !state.GetLegalActions().Any(a => a.GameType == GameType && a.Tout)) Tout = false;
        }
    }

    private void ClearSelection()
    {
        Selected = false;
        GameType = GameType.None;
        Suit = Suit.None;
        Tout = false;
    }

    public void SelectGameType(GameType type)
    {
        if (!Active || Submitted || !GameTypes.Contains(type)) return;
        ClearSelection();
        Selected = true;
        GameType = type;
    }

    public void SelectSuit(Suit suit)
    {
        if (Active && !Submitted && Suits.Contains(suit)) Suit = suit;
    }

    public void ToggleTout()
    {
        if (Active && !Submitted && GameType is GameType.Wenz or GameType.Solo) Tout = !Tout;
    }

    public bool TrySubmit()
    {
        if (!Active || Submitted || (Phase == GameState.Bidding2 && !CanSubmit)) return false;
        Submitted = true;
        Error = null;
        return true;
    }

    public void FailSubmission(string decisionId)
    {
        if (Id != decisionId) return;
        Submitted = false;
        Error = "Die Ansage konnte nicht gesendet werden. Bitte versuche es erneut.";
    }

    public Bidding2Result? Result => GameType == GameType.None ? null : new()
    {
        GameType = GameType, Suit = Suit, Tout = Tout, Sie = GameType == GameType.Sie
    };
}
