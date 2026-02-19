namespace sk.shared;

public partial class Game
{
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
            else if (_bidding1Decisions[i] == Bidding1Decision.No)
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
        if (GameState == GameState.Finished)
        {
            return new();
        }
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
            Bidding1Result = GetBidding1Result();
            GameState = GameState.Finished;
            this.CreateGameResult();
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

    private void AdvanceBidding2Turn()
    {
        do
        {
            ActivePlayer = (ActivePlayer + 1) % 4;
        }
        while (_bidding1Decisions[ActivePlayer] != Bidding1Decision.Yes);
    }
}

public sealed class BiddingStateComparer : IComparer<BiddingState>
{
    public int Compare(BiddingState? a, BiddingState? b)
    {
        int ra = Rank(a);
        int rb = Rank(b);

        return ra.CompareTo(rb);
    }

    private static int Rank(BiddingState? x) => x switch
    {
        null => 0,

        { Sie: true } => 10,
        { Tout: true } => 4,
        { ProposedGame: GameType.Solo } => 3,
        { ProposedGame: GameType.Wenz } => 2,
        { ProposedGame: GameType.Ruf } => 1,

        _ => 0
    };
}
