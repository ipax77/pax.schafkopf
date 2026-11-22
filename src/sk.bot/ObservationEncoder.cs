using sk.shared;

namespace sk.bot;

public sealed record FeatureBlock(string Name, int Offset, int Length);

/// <summary>Versioned, private observation encoding shared by demonstration and inference.</summary>
public static class ObservationEncoder
{
    public const string Version = "schafkopf-observation-v1";
    public const int FeatureCount = 1748;
    public static IReadOnlyList<FeatureBlock> Blocks { get; } = MakeBlocks();

    private static FeatureBlock[] MakeBlocks()
    {
        (string Name, int Length)[] fields = [
            ("hand", 32), ("original_hand", 32), ("phase", 5), ("contract_type", 7),
            ("contract_suit", 5), ("tout", 1), ("dealer", 4), ("leader", 4), ("active", 4),
            ("declarer", 5), ("public_partner", 6), ("hand_counts", 4), ("points", 4),
            ("trick_counts", 4), ("current_trick", 132), ("trick_number", 1), ("ran_away", 1),
            ("played_by_seat", 128), ("own_role", 4), ("remaining_suits_and_trump", 5),
            ("bidding_history", 176), ("play_history", 1184)];
        int offset = 0;
        return fields.Select(f => { var block = new FeatureBlock(f.Name, offset, f.Length); offset += f.Length; return block; }).ToArray();
    }

    public static float[] Encode(GameObservation observation)
    {
        var o = observation;
        if (o.Seat is < 0 or > 3 || o.History.Count > 40) throw new ArgumentException("Invalid observation");
        var x = new float[FeatureCount];
        int offset = 0;
        int Relative(int seat) => (seat - o.Seat + 4) % 4;
        void Category(int value, int size) { x[offset + value] = 1; offset += size; }
        void Scalar(float value) => x[offset++] = value;
        var original = o.Hand.Concat(o.History.Where(h => h.Player == o.Seat && h.Action.Kind == ActionKind.Play)
            .Select(h => h.Action.CardId)).Distinct().ToArray();
        foreach (int id in o.Hand) x[offset + id] = 1;
        offset += 32;
        foreach (int id in original) x[offset + id] = 1;
        offset += 32;
        Category((int)o.Phase, 5);
        Category((int)(o.Contract?.GameType ?? GameType.None), 7);
        Category((int)(o.Contract?.Suit ?? Suit.None), 5);
        Scalar(o.Contract?.Tout == true ? 1 : 0);
        Category(Relative(o.Dealer), 4);
        Category(Relative(o.LeadingPlayer), 4);
        Category(Relative(o.ActivePlayer), 4);
        Category(o.Contract == null ? 4 : Relative(o.Contract.Player), 5);
        Category(o.PublicTeammate >= 0 ? Relative(o.PublicTeammate) : o.Contract?.GameType == GameType.Ruf ? 4 : 5, 6);
        for (int relative = 0; relative < 4; relative++) Scalar(o.HandCounts[(o.Seat + relative) % 4] / 8f);
        for (int relative = 0; relative < 4; relative++) Scalar(o.Points[(o.Seat + relative) % 4] / 120f);
        for (int relative = 0; relative < 4; relative++) Scalar(o.TrickCounts[(o.Seat + relative) % 4] / 8f);
        for (int relative = 0; relative < 4; relative++) Category(o.CurrentTrick[(o.Seat + relative) % 4] + 1, 33);
        Scalar(o.TrickNumber / 8f);
        Scalar(o.RanAway ? 1 : 0);
        foreach (var h in o.History.Where(h => h.Action.Kind == ActionKind.Play))
            x[offset + Relative(h.Player) * 32 + h.Action.CardId] = 1;
        offset += 128;
        // Unknown, declarer, called-ace holder, defender. Derive only from own/public cards.
        int role = 0;
        if (o.Contract is { } contract)
        {
            int ace = contract.GameType == GameType.Ruf
                ? GameAction.ToCardId(new Card { Suit = contract.Suit, Rank = Rank.Ace }) : -1;
            role = contract.Player == o.Seat ? 1 : original.Contains(ace) ? 2 : 3;
        }
        Category(role, 4);
        foreach (int id in o.Hand)
        {
            var card = GameAction.FromCardId(id);
            bool trump = card.IsTrump(o.Contract?.GameType ?? GameType.Ruf, o.Contract?.Suit ?? Suit.Herz);
            x[offset + (trump ? 4 : (int)card.Suit - 1)] += 1f / 8;
        }
        offset += 5;
        var bids = o.History.Where(h => h.Action.Kind != ActionKind.Play).ToArray();
        var plays = o.History.Where(h => h.Action.Kind == ActionKind.Play).ToArray();
        if (bids.Length > 8 || plays.Length > 32) throw new ArgumentException("History exceeds encoding bounds");
        foreach (var h in bids)
        {
            Scalar(1); Category(Relative(h.Player), 4); Category(h.Action.Id, 17);
        }
        offset += (8 - bids.Length) * 22;
        foreach (var h in plays)
        {
            Scalar(1); Category(Relative(h.Player), 4); Category(h.Action.CardId, 32);
        }
        offset += (32 - plays.Length) * 37;
        if (offset != FeatureCount) throw new InvalidOperationException("Encoder layout mismatch");
        return x;
    }

    public static string ModelKind(GameObservation o) => o.Phase switch
    {
        GameState.Bidding1 or GameState.Bidding2 => "bidding",
        GameState.Playing => "playing",
        _ => throw new ArgumentException("No policy decision in this phase")
    };
    public static int LocalAction(GameObservation o, GameAction action) => action.Id - (ModelKind(o) == "playing" ? 17 : 0);
    public static GameAction EngineAction(string kind, int action) => kind switch
    {
        "bidding" when action is >= 0 and < 17 => GameAction.FromId(action),
        "playing" when action is >= 0 and < 32 => GameAction.FromId(action + 17),
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
    public static uint LegalMask(GameObservation o)
    {
        uint mask = 0;
        foreach (var action in o.LegalActions) mask |= 1u << LocalAction(o, action);
        if (mask == 0) throw new ArgumentException("No legal actions");
        return mask;
    }
}
