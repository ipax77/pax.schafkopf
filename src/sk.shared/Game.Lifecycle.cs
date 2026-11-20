namespace sk.shared;

public partial class Game
{
    public bool TryStartGame()
    {
        if (GameState != GameState.None || !Table.Players.All(p => p.IsConnected)) return false;
        StartRound((ulong)Random.Shared.NextInt64(), Dealer);
        return true;
    }

    public void DealCards() => DealCards((ulong)Random.Shared.NextInt64());

    public void DealCards(ulong seed)
    {
        Span<int> deck = stackalloc int[32];
        for (int i = 0; i < deck.Length; i++) deck[i] = i;
        var rng = new DealRandom(seed);
        for (int i = deck.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
        for (int i = 0; i < 4; i++)
        {
            var hand = new List<Card>(8);
            var startingHand = new List<Card>(8);
            for (int j = 0; j < 8; j++)
            {
                var card = GameAction.FromCardId(deck[i * 8 + j]);
                hand.Add(card);
                startingHand.Add(card);
            }
            Table.Players[i].Hand = hand;
            Table.Players[i].StartingHand = startingHand;
        }
    }

    public void CreateGameResult()
    {
        if (_settled) throw new InvalidOperationException("Round already settled.");
        if (Bidding2Result == null)
        {
            if (GameState != GameState.Finished || !_bidding1Decisions.All(d => d == Bidding1Decision.No))
                throw new InvalidOperationException("Round is not complete.");
            GameResults.Add(new());
            _settled = true;
            return;
        }
        if (GameState != GameState.Playing || Turn != 8 || Table.Players.Any(p => p.Hand.Count != 0) ||
            Table.Players.Sum(p => p.Tricks.Count) != 32)
            throw new InvalidOperationException("Round is not complete.");

        var b = Bidding2Result;
        var declarer = Table.Players[b.PlayerIndex];
        // Determine the actual team from the original deal, independently of public revelation.
        var partner = b.GameType == GameType.Ruf
            ? Table.Players.Single(p => p.StartingHand.Contains(new Card { Rank = Rank.Ace, Suit = b.Suit })) : null;
        var team = partner == null ? new[] { declarer } : new[] { declarer, partner };
        var teamHand = team.SelectMany(p => p.StartingHand).ToList();
        var taken = team.SelectMany(p => p.Tricks).ToList();
        var (runners, without) = GameScoring.CountRunners(teamHand, b.GameType, b.Suit);
        var result = new GameResult
        {
            GameType = b.GameType, Suit = b.Suit, Tout = b.Tout,
            Player = CopyPlayer(declarer.Player), Player2 = partner == null ? null : CopyPlayer(partner.Player),
            Runners = runners, WithoutRunners = without, PlayerPoints = taken.Sum(c => c.GetValue()), PlayerTricks = taken.Count / 4
        };
        result.SetGameCost();
        for (int seat = 0; seat < 4; seat++)
        {
            bool inTeam = team.Contains(Table.Players[seat]);
            int amount = result.Cost * (partner == null && inTeam ? 3 : 1);
            _rewards[seat] = inTeam == result.DeclarerWins ? amount : -amount;
            Table.Players[seat].Cash += _rewards[seat];
        }
        GameResults.Add(result);
        _settled = true;
        GameState = GameState.Finished;
    }

    // Live tables retain their session ledger; headless resets discard it.
    public void Reset() => StartRound((ulong)Random.Shared.NextInt64(), (Dealer + 1) % 4);

    public void ResetEpisode(ulong seed, int dealer = 3)
    {
        if (dealer is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(dealer));
        GameResults.Clear();
        foreach (var p in Table.Players) p.Cash = 0;
        StartRound(seed, dealer);
    }

    private void StartRound(ulong seed, int dealer)
    {
        Revision++;
        Turn = 0;
        ReadyCheck = 0;
        Table.Reset();
        Array.Clear(_bidding1Decisions);
        _bidding2States.Clear();
        _history.Clear();
        Array.Clear(_rewards);
        _settled = false;
        Bidding1Result = null;
        Bidding2Result = null;
        DrunterDurch = false;
        PublicTeammate = -1;
        Dealer = dealer;
        ActivePlayer = leadingPlayer = (Dealer + 1) % 4;
        DealCards(seed);
        GameState = GameState.Bidding1;
    }
}

// Versioned by the engine contract: SplitMix64 + unbiased Fisher-Yates, independent of System.Random.
internal struct DealRandom(ulong seed)
{
    private ulong _state = seed;
    private ulong NextUInt64()
    {
        unchecked
        {
            ulong z = (_state += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
    public int Next(int bound)
    {
        ulong range = (ulong)bound;
        ulong threshold = unchecked(0UL - range) % range;
        ulong value;
        do value = NextUInt64(); while (value < threshold);
        return (int)(value % range);
    }
}
