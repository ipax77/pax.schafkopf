namespace sk.shared;

public static class GameExtensions
{
    public static bool TryAssignEmptySeat(this Game game, Player player)
    {
        var emptySlot = game.Table.Players.OrderBy(o => o.Position).FirstOrDefault(f => f.Player.Guid == Guid.Empty);
        if (emptySlot is null)
        {
            return false;
        }
        emptySlot.Player = player;
        emptySlot.IsConnected = true;
        return true;
    }

    public static bool HasPlayer(this Game game, Player player)
    {
        var slot = game.Table.Players.FirstOrDefault(f => f.Player.Guid == player.Guid);
        return slot is not null;
    }

    public static bool TryStartGame(this Game game)
    {
        if (!game.Table.Players.All(a => a.IsConnected))
        {
            return false;
        }
        game.DealCards();
        game.GameState = GameState.Bidding1;
        return true;
    }

    public static TablePlayer? GetTablePlayer(this Game game, Guid playerId)
    {
        return game.Table.Players.FirstOrDefault(f => f.Player.Guid == playerId);
    }

    public static PublicGameState ToPublicGameState(this Game game, int forPlayer)
    {
        var publicTable = new Table
        {
            Guid = game.Table.Guid,
            PreviouseTrick = game.Table.PreviouseTrick,
            CurrentTrick = game.Table.CurrentTrick,
            CurrentTrickCard = game.Table.CurrentTrickCard,
        };

        for (int i = 0; i < 4; i++)
        {
            var originalPlayer = game.Table.Players[i];
            publicTable.Players[i] = new TablePlayer
            {
                Player = originalPlayer.Player,
                Position = originalPlayer.Position,
                Tricks = originalPlayer.Tricks,
                Hand = i == forPlayer ? originalPlayer.Hand : GetPrivateHand(originalPlayer.Hand),
                IsConnected = originalPlayer.IsConnected
            };
        }

        return new PublicGameState
        {
            ShortCode = game.ShortCode,
            GameState = game.GameState,
            ActivePlayer = game.ActivePlayer,
            YourPosition = forPlayer,
            Turn = game.Turn,
            Bidding1Result = game.GetPublicBidding1Result(),
            Bidding2Result = game.GetPublicBidding2Result(),
            Table = publicTable
        };
    }

    private static List<Card> GetPrivateHand(List<Card> hand)
    {
        List<Card> cards = [];
        foreach (var card in hand)
        {
            cards.Add(new() { Rank = Rank.Seven, Suit = Suit.None });
        }
        return cards;
    }

    private static readonly Rank[] Ranks =
    [
        Rank.Seven, Rank.Eight, Rank.Nine,
            Rank.Unter, Rank.Ober,
            Rank.King, Rank.Ten, Rank.Ace
    ];

    private static readonly Suit[] Suits =
        [
        Suit.Schellen, Suit.Herz, Suit.Gras, Suit.Eichel
        ];

    public static void CollectTrick(this Game game)
    {
        ArgumentNullException.ThrowIfNull(game.Bidding2Result);

        var trick = new List<(int Player, Card Card)>();

        int player = game.ActivePlayer;

        for (int i = 0; i < 4; i++)
        {
            var card = game.Table.CurrentTrick[player];
            ArgumentNullException.ThrowIfNull(card);

            trick.Add((player, card));
            player = (player + 1) % 4;
        }

        var firstCard = trick[0].Card;
        var comparer = new TrickCardComparer(game.Bidding2Result.GameType, game.Bidding2Result.Suit, firstCard);
        var winner = trick.OrderByDescending(o => o.Card, comparer).First();

        // store trick in play order, not sorted order
        foreach (var (_, card) in trick)
            game.Table.Players[winner.Player].Tricks.Add(card);

        game.Table.PreviouseTrick = game.Table.CurrentTrick;
        game.Table.CurrentTrick = [null, null, null, null];

        game.ActivePlayer = winner.Player;
    }

    public static void DealCards(this Game game)
    {
        var deck = CreateDeck().Shuffle();
        var hands = deck.Chunk(8).ToArray();

        for (int i = 0; i < 4; i++)
        {
            game.Table.Players[i].Hand = [.. hands[i]];

        }
    }

    private static Card[] CreateDeck()
    {
        var deck = new Card[32];
        int i = 0;

        foreach (var suit in Suits)
            foreach (var rank in Ranks)
                deck[i++] = new Card { Rank = rank, Suit = suit };

        return deck;
    }
}
