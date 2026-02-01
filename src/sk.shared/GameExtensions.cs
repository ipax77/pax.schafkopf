namespace sk.shared;

public static class GameExtensions
{
    public static PublicGameState ToPublicGameState(this Game game, int forPlayer = -1)
    {
        var publicTable = new Table
        {
            Guid = game.Table.Guid,
            PreviouseTrick = game.Table.PreviouseTrick,
            CurrentTrick = game.Table.CurrentTrick
        };

        for (int i = 0; i < 4; i++)
        {
            var originalPlayer = game.Table.Players[i];
            publicTable.Players[i] = new TablePlayer
            {
                Player = originalPlayer.Player,
                Position = originalPlayer.Position,
                Tricks = originalPlayer.Tricks,
                Hand = i == forPlayer ? originalPlayer.Hand : new List<Card>()
            };
        }

        return new PublicGameState
        {
            GameState = game.GameState,
            ActivePlayer = game.ActivePlayer,
            Turn = game.Turn,
            Bidding1Result = game.Bidding1Result,
            Bidding2Result = game.Bidding2Result,
            Table = publicTable
        };
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
