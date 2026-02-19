namespace sk.shared;

public partial class PublicGameState
{
    public string ShortCode { get; set; } = string.Empty;
    public GameState GameState { get; set; }
    public int LeadingPlayer { get; set; }
    public int ActivePlayer { get; set; }
    public int? YourPosition { get; set; }
    public int Turn { get; set; }
    public Bidding1Result? Bidding1Result { get; set; }
    public Bidding2Result? Bidding2Result { get; set; }
    public bool DrunterDurch { get; set; }
    public int PublicTeammate { get; set; } = -1;
    public Table Table { get; set; } = new();
    public PublicGameResult? PublicGameResult { get; set; }
}
