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
            GameState.Playing => GetLegalPlayActions(),
            _ => []
        };
    }

    private List<GameAction> GetLegalPlayActions()
    {
        var cards = Table.Players[ActivePlayer].GetValidCards(this);
        var actions = new List<GameAction>(cards.Count);
        for (int i = 0; i < cards.Count; i++) actions.Add(GameAction.Play(cards[i]));
        return actions;
    }

    private static readonly StepResult Continuing = new(false, Array.AsReadOnly(new int[4]));

    public StepResult Step(int player, GameAction action)
    {
        if (player != ActivePlayer) throw new InvalidOperationException("Wrong player.");
        // Check command shape here; the public handlers remain the authority for phase and legality.
        // Do not construct a second complete legal-action list before those handlers validate.
        switch (action.Kind)
        {
            case ActionKind.Interest when action == GameAction.Interest(action.WouldPlay):
                SetBidding1(player, new() { WouldPlay = action.WouldPlay }); break;
            case ActionKind.Pass when action == GameAction.Pass:
                SetBidding2(player, new()); break;
            case ActionKind.Bid when action.GameType != GameType.None &&
                !(action.GameType == GameType.Sie && action.Tout) &&
                action == GameAction.Bid(action.GameType, action.Suit, action.Tout):
                SetBidding2(player, new() { ProposedGame = action.GameType, ProposedSuit = action.Suit, Tout = action.Tout }); break;
            case ActionKind.Play when action.CardId is >= 0 and < 32 &&
                action == new GameAction(ActionKind.Play, CardId: action.CardId):
                PlayCard(player, GameAction.FromCardId(action.CardId)); break;
            default: throw new InvalidOperationException("Illegal action.");
        }
        return GameState == GameState.Finished
            ? new(true, Array.AsReadOnly((int[])_rewards.Clone())) : Continuing;
    }

    /// <summary>Detached, seat-limited observation. Never reveals terminal opponent hands.</summary>
    public GameObservation Observe(int seat)
    {
        if (seat is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(seat));
        var bid = Bidding2Result ?? GetPublicBidding2Result();
        var legal = seat == ActivePlayer ? GetLegalActions() : [];
        var ownHand = Table.Players[seat].Hand;
        var hand = new int[ownHand.Count];
        for (int i = 0; i < hand.Length; i++) hand[i] = GameAction.ToCardId(ownHand[i]);
        var handCounts = new int[4];
        var trick = new int[4];
        var points = new int[4];
        var trickCounts = new int[4];
        for (int i = 0; i < 4; i++)
        {
            var player = Table.Players[i];
            handCounts[i] = player.Hand.Count;
            trick[i] = Table.CurrentTrick[i] is { } card ? GameAction.ToCardId(card) : -1;
            for (int j = 0; j < player.Tricks.Count; j++) points[i] += player.Tricks[j].GetValue();
            trickCounts[i] = player.Tricks.Count / 4;
        }
        return new(seat, Dealer, ActivePlayer, leadingPlayer, Turn, GameState, PublicTeammate, DrunterDurch,
            bid == null ? null : new(bid.PlayerIndex, bid.GameType, bid.Suit, bid.Tout),
            Array.AsReadOnly(hand), Array.AsReadOnly(handCounts), Array.AsReadOnly(trick),
            Array.AsReadOnly(points), Array.AsReadOnly(trickCounts),
            Array.AsReadOnly(_history.ToArray()), legal.AsReadOnly());
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
