namespace sk.shared;

public partial class Game
{
    public Bidding1Result? GetPublicBidding1Result()
    {
        if (Bidding1Result is not null)
        {
            return new() { InterestedPlayers = Bidding1Result.InterestedPlayers.ToArray(), DeclinedPlayers = Bidding1Result.DeclinedPlayers.ToArray() };
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
        if (Bidding2Result != null)
            return CopyBid(Bidding2Result);

        if (_bidding2States.Count == 0)
            return null;
        var highestBid = HighestBid();
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
        ArgumentNullException.ThrowIfNull(command);
        if (command.ProposedGame is not (null or GameType.None) || command.ProposedSuit is not (null or Suit.None) || command.Tout || command.Sie)
            throw new InvalidOperationException("Interest must not include a contract.");
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
        Revision++;
        _history.Add(new(playerIndex, GameAction.Interest(command.WouldPlay)));

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
        ArgumentNullException.ThrowIfNull(command);
        if (GameState != GameState.Bidding2)
            throw new InvalidOperationException("Not in bidding phase 2.");

        ArgumentNullException.ThrowIfNull(Bidding1Result);

        if (playerIndex != ActivePlayer)
            throw new InvalidOperationException("Not this player's turn.");

        var type = command.ProposedGame ?? GameType.None;
        var suit = command.ProposedSuit ?? Suit.None;
        // Sie is a contract, not an independently selectable modifier. Accept legacy Tout on Sie.
        if (command.Sie && type != GameType.Sie)
            throw new InvalidOperationException("Sie requires the Sie contract.");
        var action = type == GameType.None ? GameAction.Pass : GameAction.Bid(type, suit, type != GameType.Sie && command.Tout);
        if (type == GameType.None && (suit != Suit.None || command.Tout || command.Sie) ||
            !GetLegalActions().Contains(action))
            throw new InvalidOperationException("Illegal bid.");
        var accepted = new BiddingState { WouldPlay = type != GameType.None, ProposedGame = type,
            ProposedSuit = suit, Tout = action.Tout, Sie = type == GameType.Sie };
        _bidding2States.Add((playerIndex, accepted));
        Revision++;
        _history.Add(new(playerIndex, action));

        if (_bidding2States.Count == Bidding1Result.InterestedPlayers.Count)
        {
            var highestBid = HighestBid();
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

    private (int, BiddingState) HighestBid()
    {
        var highest = _bidding2States[0];
        int rank = GameRules.BidRank(highest.Item2.ProposedGame ?? GameType.None, highest.Item2.Tout);
        for (int i = 1; i < _bidding2States.Count; i++)
        {
            var bid = _bidding2States[i];
            int candidate = GameRules.BidRank(bid.Item2.ProposedGame ?? GameType.None, bid.Item2.Tout);
            if (candidate > rank) { highest = bid; rank = candidate; }
        }
        return highest;
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

    private static int Rank(BiddingState? x) => x == null ? 0 : GameRules.BidRank(x.ProposedGame ?? GameType.None, x.Tout);
}
