
namespace sk.shared;

public sealed class Game
{
    public Table Table { get; set; } = new();
    public GameState GameState { get; set; }
    public int ActivePlayer { get; private set; }

    public Bidding1Result? Bidding1Result { get; set; }
    public BiddingState? BiddingState { get; set; }

    private readonly List<int> _bidding1Interested = [];
    private int _bidding1Count;

    public void SetBidding1(int playerIndex, BiddingState command)
    {
        if (GameState != GameState.Bidding1)
            throw new InvalidOperationException("Not in bidding phase 1.");

        if (playerIndex != ActivePlayer)
            throw new InvalidOperationException("Not this player's turn.");

        _bidding1Count++;

        if (command.WouldPlay)
            _bidding1Interested.Add(playerIndex);

        AdvanceTurn();

        // phase complete
        if (_bidding1Count == 4)
        {
            if (_bidding1Interested.Count == 0)
            {
                GameState = GameState.Finished;
                return;
            }
            Bidding1Result = new()
            {
                InterestedPlayers = _bidding1Interested,
                LastInterestedPlayer = _bidding1Interested.Last()
            };
            GameState = GameState.Bidding2;
        }
    }

    public void SetBidding2(int playerIndex, BiddingState command)
    {
        ArgumentNullException.ThrowIfNull(Bidding1Result);
        if (playerIndex != Bidding1Result.LastInterestedPlayer)
        {
            throw new InvalidOperationException("Not this player's turn.");
        }

        if (!command.ProposedGame.HasValue)
        {
            throw new ArgumentNullException(nameof(command.ProposedGame));
        }

        BiddingState = command;
        GameState = GameState.Playing;
    }


    private void AdvanceTurn()
    {
        ActivePlayer = (ActivePlayer + 1) % 4;
    }

    public void Reset()
    {
        foreach (var player in Table.Players)
        {
            player.Reset();
        }
        Bidding1Result = null;
        AdvanceTurn();
        GameState = GameState.Bidding1;
    }
}

public sealed class Bidding1Result
{
    public IReadOnlyList<int> InterestedPlayers { get; init; } = [];
    public int LastInterestedPlayer { get; init; }
}


public sealed class TablePlayer
{
    public Player Player { get; set; } = new();
    public byte Position { get; init; }
    public List<Card> Hand { get; set; } = [];
    public List<Card> Tricks { get; set; } = [];

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
