namespace sk.shared;

public partial class Game
{
    private readonly List<ActionEvent> _history = [];
    private readonly int[] _rewards = new int[4];
    private bool _settled;
    public long Revision { get; private set; }

    public List<GameAction> GetLegalActions()
    {
        return GameState switch
        {
            GameState.Bidding1 => [GameAction.Interest(false), GameAction.Interest(true)],
            GameState.Bidding2 => GameRules.LegalBids(Table.Players[ActivePlayer].Hand,
                _bidding2States.Count < Bidding1Result!.InterestedPlayers.Count - 1 ||
                _bidding2States.Any(b => b.Item2.ProposedGame is not (null or GameType.None))),
            GameState.Playing => Table.Players[ActivePlayer].GetValidCards(this).Select(GameAction.Play).ToList(),
            _ => []
        };
    }

    public StepResult Step(int player, GameAction action)
    {
        if (player != ActivePlayer || !GetLegalActions().Contains(action))
            throw new InvalidOperationException("Illegal action or wrong player.");
        switch (action.Kind)
        {
            case ActionKind.Interest: SetBidding1(player, new() { WouldPlay = action.WouldPlay }); break;
            case ActionKind.Pass: SetBidding2(player, new()); break;
            case ActionKind.Bid: SetBidding2(player, new() { ProposedGame = action.GameType, ProposedSuit = action.Suit, Tout = action.Tout }); break;
            case ActionKind.Play: PlayCard(player, GameAction.FromCardId(action.CardId)); break;
        }
        return new(GameState == GameState.Finished, Array.AsReadOnly((int[])_rewards.Clone()));
    }

    /// <summary>Detached, seat-limited observation. Never reveals terminal opponent hands.</summary>
    public GameObservation Observe(int seat)
    {
        if (seat is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(seat));
        var bid = GetPublicBidding2Result();
        var legal = seat == ActivePlayer ? GetLegalActions() : [];
        return new(seat, Dealer, ActivePlayer, leadingPlayer, Turn, GameState, PublicTeammate, DrunterDurch,
            bid == null ? null : new(bid.PlayerIndex, bid.GameType, bid.Suit, bid.Tout),
            Array.AsReadOnly(Table.Players[seat].Hand.Select(GameAction.ToCardId).ToArray()),
            Array.AsReadOnly(Table.Players.Select(p => p.Hand.Count).ToArray()),
            Array.AsReadOnly(Table.CurrentTrick.Select(c => c == null ? -1 : GameAction.ToCardId(c)).ToArray()),
            Array.AsReadOnly(Table.Players.Select(p => p.Tricks.Sum(c => c.GetValue())).ToArray()),
            Array.AsReadOnly(Table.Players.Select(p => p.Tricks.Count / 4).ToArray()),
            Array.AsReadOnly(_history.ToArray()), Array.AsReadOnly(legal.ToArray()));
    }

    private static Bidding2Result? CopyBid(Bidding2Result? b) => b == null ? null : new()
    { PlayerIndex = b.PlayerIndex, GameType = b.GameType, Suit = b.Suit, Tout = b.Tout, Sie = b.Sie };
}

public readonly record struct ActionEvent(int Player, GameAction Action);
public sealed record ContractObservation(int Player, GameType GameType, Suit Suit, bool Tout);
public sealed record StepResult(bool Terminal, IReadOnlyList<int> Rewards);
public sealed record GameObservation(int Seat, int Dealer, int ActivePlayer, int LeadingPlayer, int TrickNumber,
    GameState Phase, int PublicTeammate, bool RanAway, ContractObservation? Contract,
    IReadOnlyList<int> Hand, IReadOnlyList<int> HandCounts, IReadOnlyList<int> CurrentTrick,
    IReadOnlyList<int> Points, IReadOnlyList<int> TrickCounts, IReadOnlyList<ActionEvent> History,
    IReadOnlyList<GameAction> LegalActions)
{
    public bool[] LegalActionMask()
    {
        var mask = new bool[GameAction.ActionCount];
        foreach (var a in LegalActions) mask[a.Id] = true;
        return mask;
    }
}

/// <summary>One isolated episode. Single owner; use separate instances for parallel workers.</summary>
public sealed class HeadlessGame
{
    public const string RulesVersion = "schafkopf-long-v1";
    private readonly Game _game = new();
    public int CurrentPlayer => _game.ActivePlayer;
    public bool IsTerminal => _game.GameState == GameState.Finished;
    public void Reset(ulong seed, int dealer = 3) => _game.ResetEpisode(seed, dealer);
    public IReadOnlyList<GameAction> LegalActions() => _game.GetLegalActions().AsReadOnly();
    public GameObservation Observe(int seat) => _game.Observe(seat);
    public StepResult Step(GameAction action) => _game.Step(CurrentPlayer, action);
    public StepResult Step(int actionId) => Step(GameAction.FromId(actionId));
}

public interface IGamePolicy
{
    GameAction ChooseAction(GameObservation observation);
}
