
namespace sk.shared;

public sealed class TablePlayer
{
    public Player Player { get; set; } = new();
    public byte Position { get; init; }
    public List<Card> StartingHand { get; set; } = [];
    public List<Card> Hand { get; set; } = [];
    public List<Card> Tricks { get; set; } = [];
    public bool IsConnected { get; set; }
    public int Cash { get; set; }
    public bool ReadyForNextRound { get; set; }

    public void Reset()
    {
        StartingHand.Clear();
        Hand.Clear();
        Tricks.Clear();
        ReadyForNextRound = false;
    }

    public List<Card> GetValidCards(Game game)
    {
        ArgumentNullException.ThrowIfNull(game.Bidding2Result);
        var playedCards = game.Table.CurrentTrick.Count(c => c != null);
        if (playedCards == 0)
        {
            return Hand;
        }

        var firstPlayer = game.leadingPlayer;
        var firstCard = game.Table.CurrentTrick[firstPlayer];
        ArgumentNullException.ThrowIfNull(firstCard);
        var mustFollow = Hand
            .Where(c => c.CanOperate(firstCard,
                game.Bidding2Result.GameType,
                game.Bidding2Result.Suit, Hand))
            .ToList();

        // if player cannot follow suit / trump → free choice
        return mustFollow.Count > 0
            ? mustFollow
            : Hand;
    }
}
