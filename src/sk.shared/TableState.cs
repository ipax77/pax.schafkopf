
namespace sk.shared;

public sealed class Game
{
    public Table Table { get; set; } = new();
    public GameState GameState { get; set; }
    public GameType GameType { get; set; }
    public Suit Trump { get; set; }
    public int ActivePlayer { get; set; }
}

public sealed class TablePlayer
{
    public Player Player { get; set; } = new();
    public byte Position { get; init; }
    public List<Card> Hand { get; set; } = [];
    public List<Card> Tricks { get; set; } = [];

    public BiddingState Bidding { get; } = new();
}

public sealed class BiddingState
{
    public bool WouldPlay { get; set; }
    public GameType? ProposedGame { get; set; }
    public Suit? ProposedSuit { get; set; }
}


public sealed class Player
{
    public string Name { get; set; } = string.Empty;
    public Guid Guid { get; set; }
}

public sealed class Table
{
    public Guid Guid { get; set; }
    public TablePlayer[] Players { get; set; } = [
        new TablePlayer() { Position = 0 },
        new TablePlayer() { Position = 1 },
        new TablePlayer() { Position = 2 },
        new TablePlayer() { Position = 3 },
    ];
    public Card?[] PreviouseTrick { get; set; } = [null, null, null, null];
    public Card?[] CurrentTrick { get; set; } = [null, null, null, null];
}

public sealed class Card
{
    public Rank Rank { get; init; }
    public Suit Suit { get; init; }
}
