
namespace sk.shared;

public sealed class Game
{
    public Table Table { get; set; } = new();
    public GameState GameState { get; set; }
    public int ActivePlayer { get; set; }
    public int Turn { get; set; }

    public Bidding1Result? Bidding1Result { get; set; }
    public Bidding2Result? Bidding2Result { get; set; }

    private readonly List<int> _bidding1Interested = [];
    private int _bidding1Count;
    private int _firstPlayer;
    private readonly List<(int, BiddingState)> _bidding2States = [];

    public Bidding1Result? GetPublicBidding1Result()
    {
        if (Bidding1Result is not null)
        {
            return Bidding1Result;
        }
        else
        {
            if (GameState == GameState.Bidding1)
            {
                return new()
                {
                    InterestedPlayers = _bidding1Interested
                };
            }
            else if (GameState == GameState.Bidding2)
            {
                return new()
                {
                    InterestedPlayers = _bidding2States
                        .Where(x => x.Item2.WouldPlay)
                        .Select(s => s.Item1)
                        .ToList()
                };
            }
        }
        return Bidding1Result;
    }

    public Bidding2Result? GetPublicBidding2Result()
    {
        return Bidding2Result;
    }

    public void SetBidding1(int playerIndex, BiddingState command)
    {
        if (GameState != GameState.Bidding1)
            throw new InvalidOperationException("Not in bidding phase 1.");

        if (playerIndex != ActivePlayer)
            throw new InvalidOperationException("Not this player's turn.");

        if (_bidding1Count == 0)
        {
            _firstPlayer = playerIndex;
        }

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
                FirstPlayer = _firstPlayer,
                InterestedPlayers = _bidding1Interested,
                LastInterestedPlayer = _bidding1Interested.Last()
            };
            ActivePlayer = Bidding1Result.InterestedPlayers[0];
            GameState = GameState.Bidding2;
        }
    }

    public void SetBidding2(int playerIndex, BiddingState command)
    {
        if (GameState != GameState.Bidding2)
            throw new InvalidOperationException("Not in bidding phase 2.");

        ArgumentNullException.ThrowIfNull(Bidding1Result);

        if (playerIndex != ActivePlayer)
            throw new InvalidOperationException("Not this player's turn.");

        _bidding2States.Add((playerIndex, command));

        if (_bidding2States.Count == Bidding1Result.InterestedPlayers.Count)
        {
            var comparer = new BiddingStateComparer();
            var highestBid = _bidding2States.OrderByDescending(o => o.Item2, comparer).FirstOrDefault();
            ArgumentNullException.ThrowIfNull(highestBid.Item2);
            ArgumentNullException.ThrowIfNull(highestBid.Item2.ProposedGame);

            Bidding2Result = new()
            {
                PlayerIndex = highestBid.Item1,
                GameType = highestBid.Item2.ProposedGame.Value,
                Suit = highestBid.Item2.ProposedSuit ?? Suit.None,
                Sie = highestBid.Item2.Sie,
                Tout = highestBid.Item2.Tout
            };
            ActivePlayer = Bidding1Result.FirstPlayer;
            GameState = GameState.Playing;
        }
        else
        {
            AdvanceBidding2Turn();
        }
    }

    public void PlayCard(int playerIndex, Card card)
    {
        if (playerIndex != ActivePlayer)
            throw new InvalidOperationException("Not this player's turn.");

        Table.Players[playerIndex].Hand.Remove(card);
        Table.CurrentTrick[playerIndex] = card;

        if (Table.CurrentTrick.All(a => a != null))
        {
            this.CollectTrick();
            Turn++;
        }
        else
        {
            AdvanceTurn();
        }
        Table.CurrentTrickCard = new() { Position = playerIndex, Card = card };
    }

    private void AdvanceTurn()
    {
        ActivePlayer = (ActivePlayer + 1) % 4;
    }

    private void AdvanceBidding2Turn()
    {
        ArgumentNullException.ThrowIfNull(Bidding1Result);
        do
        {
            ActivePlayer = (ActivePlayer + 1) % 4;
        }
        while (!Bidding1Result.InterestedPlayers.Contains(ActivePlayer));
    }


    public void Reset()
    {
        foreach (var player in Table.Players)
        {
            player.Reset();
        }
        _bidding1Count = 0;
        _firstPlayer = 0;
        _bidding1Interested.Clear();
        _bidding2States.Clear();
        Bidding1Result = null;
        Bidding2Result = null;
        Table.Reset();
        AdvanceTurn();
        GameState = GameState.Bidding1;
    }
}

public sealed class Bidding1Result
{
    public int FirstPlayer { get; init; }
    public IReadOnlyList<int> InterestedPlayers { get; init; } = [];
    public int LastInterestedPlayer { get; init; }
}

public sealed class Bidding2Result
{
    public int PlayerIndex { get; set; }
    public GameType GameType { get; set; }
    public Suit Suit { get; set; }
    public bool Sie { get; set; }
    public bool Tout { get; set; }
}

public sealed class TablePlayer
{
    public Player Player { get; set; } = new();
    public byte Position { get; init; }
    public List<Card> Hand { get; set; } = [];
    public List<Card> Tricks { get; set; } = [];
    public bool IsConnected { get; set; } = true;

}

public sealed class BiddingState
{
    public bool WouldPlay { get; set; }
    public GameType? ProposedGame { get; set; }
    public Suit? ProposedSuit { get; set; }
    public bool Sie { get; set; }
    public bool Tout { get; set; }
}


public sealed class Player
{
    public string Name { get; set; } = string.Empty;
    public Guid Guid { get; set; }
    public string? ConnectionId { get; set; }
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
    public PlayerCard? CurrentTrickCard { get; set; }
    public Card?[]? PreviouseTrick { get; set; }
    public Card?[] CurrentTrick { get; set; } = [null, null, null, null];

    public void Reset()
    {
        CurrentTrickCard = null;
    }
}

public sealed record Card
{
    public Rank Rank { get; init; }
    public Suit Suit { get; init; }
}

public sealed class PlayerCard
{
    public int Position { get; init; }
    public Card Card { get; init; } = new();
}