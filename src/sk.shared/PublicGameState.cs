namespace sk.shared;

public class PublicGameState
{
    public GameState GameState { get; set; }
    public int ActivePlayer { get; set; }
    public int? YourPosition { get; set; }
    public int Turn { get; set; }
    public Bidding1Result? Bidding1Result { get; set; }
    public Bidding2Result? Bidding2Result { get; set; }
    public Table Table { get; set; } = new();
}
