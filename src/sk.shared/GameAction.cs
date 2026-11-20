namespace sk.shared;

public enum ActionKind { Interest, Pass, Bid, Play }

/// <summary>Value-only commands. Stable IDs: interest 0/1, pass 2, bids 3..16, cards 17..48.</summary>
public readonly record struct GameAction(ActionKind Kind, bool WouldPlay = false,
    GameType GameType = GameType.None, Suit Suit = Suit.None, bool Tout = false, int CardId = -1)
{
    // Cards have init-only properties; callers can use `with` without mutating these instances.
    private static readonly Card[] Cards = Enumerable.Range(0, 32)
        .Select(id => new Card { Suit = (Suit)(id / 8 + 1), Rank = (Rank)(id % 8 + 1) }).ToArray();
    public const int ActionCount = 49;
    public static GameAction Interest(bool yes) => new(ActionKind.Interest, WouldPlay: yes);
    public static GameAction Pass => new(ActionKind.Pass);
    public static GameAction Bid(GameType type, Suit suit = Suit.None, bool tout = false) => new(ActionKind.Bid, GameType: type, Suit: suit, Tout: tout);
    public static GameAction Play(Card card) => new(ActionKind.Play, CardId: ToCardId(card));
    public static GameAction FromId(int id) => id switch
    {
        0 or 1 => Interest(id == 1),
        2 => Pass,
        3 => Bid(GameType.Ruf, Suit.Schellen),
        4 => Bid(GameType.Ruf, Suit.Gras),
        5 => Bid(GameType.Ruf, Suit.Eichel),
        6 or 7 => Bid(GameType.Wenz, tout: id == 7),
        >= 8 and <= 15 => Bid(GameType.Solo, (Suit)((id - 8) / 2 + 1), id % 2 == 1),
        16 => Bid(GameType.Sie),
        >= 17 and < ActionCount => Play(FromCardId(id - 17)),
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };
    public int Id => Kind switch
    {
        ActionKind.Interest => WouldPlay ? 1 : 0,
        ActionKind.Pass => 2,
        ActionKind.Bid when GameType == GameType.Ruf => Suit switch { Suit.Schellen => 3, Suit.Gras => 4, Suit.Eichel => 5, _ => throw new InvalidOperationException() },
        ActionKind.Bid when GameType == GameType.Wenz => Tout ? 7 : 6,
        ActionKind.Bid when GameType == GameType.Solo && Suit is >= Suit.Schellen and <= Suit.Eichel => 8 + ((int)Suit - 1) * 2 + (Tout ? 1 : 0),
        ActionKind.Bid when GameType == GameType.Sie => 16,
        ActionKind.Play when CardId is >= 0 and < 32 => 17 + CardId,
        _ => throw new InvalidOperationException("Invalid action")
    };
    public static int ToCardId(Card card)
    {
        if (card.Suit is < Suit.Schellen or > Suit.Eichel || card.Rank is < Rank.Seven or > Rank.Ace)
            throw new ArgumentException("Not a real card", nameof(card));
        return ((int)card.Suit - 1) * 8 + (int)card.Rank - 1;
    }
    public static Card FromCardId(int id) => id is >= 0 and < 32
        ? Cards[id]
        : throw new ArgumentOutOfRangeException(nameof(id));
}
