
namespace sk.shared;

public sealed class Game
{
    public string ShortCode { get; set; } = string.Empty;
    public Table Table { get; set; } = new();
    public GameState GameState { get; set; }
    public int ActivePlayer { get; set; }
    public int Turn { get; set; }

    public Bidding1Result? Bidding1Result { get; set; }
    public Bidding2Result? Bidding2Result { get; set; }

    private int leadingPlayer;
    private readonly Bidding1Decision?[] _bidding1Decisions = new Bidding1Decision?[4];
    private readonly List<(int, BiddingState)> _bidding2States = [];

    public Bidding1Result? GetPublicBidding1Result()
    {
        if (Bidding1Result is not null)
        {
            return Bidding1Result;
        }
        else if (GameState == GameState.Bidding1)
        {
            return GetBidding1Result();
        }
        else
        {
            return null;
        }
    }

    private Bidding1Result GetBidding1Result()
    {
        var interested = new List<int>();
        var declined = new List<int>();

        for (int i = 0; i < 4; i++)
        {
            if (_bidding1Decisions[i] == Bidding1Decision.Yes)
                interested.Add(i);
            else
                declined.Add(i);
        }
        return new()
        {
            InterestedPlayers = interested,
            DeclinedPlayers = declined
        };
    }

    public Bidding2Result? GetPublicBidding2Result()
    {
        if (Bidding2Result != null)
            return Bidding2Result;

        if (_bidding2States.Count == 0)
            return null;
        var comparer = new BiddingStateComparer();
        var highestBid = _bidding2States.OrderByDescending(o => o.Item2, comparer).FirstOrDefault();
        return new()
        {
            PlayerIndex = highestBid.Item1,
            GameType = highestBid.Item2.ProposedGame.HasValue ? highestBid.Item2.ProposedGame.Value : GameType.None,
            Suit = highestBid.Item2.ProposedSuit ?? Suit.None,
            Sie = highestBid.Item2.Sie,
            Tout = highestBid.Item2.Tout
        };
    }

    public void SetBidding1(int playerIndex, BiddingState command)
    {
        if (GameState != GameState.Bidding1)
            throw new InvalidOperationException("Not in bidding phase 1.");

        if (playerIndex != ActivePlayer)
            throw new InvalidOperationException("Not this player's turn.");

        if (_bidding1Decisions[playerIndex].HasValue)
            throw new InvalidOperationException("Player already acted.");

        if (!_bidding1Decisions.Any(a => a.HasValue))
        {
            leadingPlayer = playerIndex;
        }

        var decision = command.WouldPlay
            ? Bidding1Decision.Yes
            : Bidding1Decision.No;

        _bidding1Decisions[playerIndex] = decision;

        if (!_bidding1Decisions.All(a => a.HasValue))
        {
            AdvanceTurn();
            return;
        }

        if (_bidding1Decisions.All(a => a == Bidding1Decision.No))
        {
            GameState = GameState.Finished;
            return;
        }

        Bidding1Result = GetBidding1Result();
        GameState = GameState.Bidding2;
        ActivePlayer = leadingPlayer;
        if (_bidding1Decisions[ActivePlayer] != Bidding1Decision.Yes)
        {
            AdvanceBidding2Turn();
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
            ActivePlayer = leadingPlayer;
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
        do
        {
            ActivePlayer = (ActivePlayer + 1) % 4;
        }
        while (_bidding1Decisions[ActivePlayer] != Bidding1Decision.Yes);
    }

    public void Reset()
    {
        foreach (var player in Table.Players)
        {
            player.Reset();
        }
        Array.Fill(_bidding1Decisions, null);
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
    public IReadOnlyList<int> InterestedPlayers { get; init; } = [];
    public IReadOnlyList<int> DeclinedPlayers { get; init; } = [];
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
    public bool IsConnected { get; set; }

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

public record PlayerViewInfo(TablePlayer TablePlayer, int ServerIndex, int ViewIndex);