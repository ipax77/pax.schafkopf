
namespace sk.shared;

public sealed class TablePlayer
{
    public Player Player { get; set; } = new();
    public byte Position { get; init; }
    public List<Card> StartingHand { get; set; } = [];
    public List<Card> Hand { get; set; } = [];
    public List<Card> Tricks { get; set; } = [];
    public bool IsConnected { get; set; }
    // Assigned only by the server; independent of browser connection state.
    public bool IsBot { get; set; }
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
        return game.GameState == GameState.Playing && game.ActivePlayer == Position && game.Bidding2Result != null
            ? GameRules.LegalCards(Hand, game.Bidding2Result, game.Table.CurrentTrick[game.leadingPlayer], game.DrunterDurch)
            : [];
    }
}
