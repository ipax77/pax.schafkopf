namespace sk.shared;

public partial class Game
{
    public string ShortCode { get; set; } = string.Empty;
    public Table Table { get; set; } = new();
    public GameState GameState { get; set; }
    public int Dealer { get; set; } = 3;
    public int ActivePlayer { get; set; }
    public int Turn { get; set; }
    public int ReadyCheck { get; set; }

    public Bidding1Result? Bidding1Result { get; set; }
    public Bidding2Result? Bidding2Result { get; set; }
    public bool DrunterDurch { get; private set; }
    public int PublicTeammate { get; private set; } = -1;

    public int leadingPlayer;
    private readonly Bidding1Decision?[] _bidding1Decisions = new Bidding1Decision?[4];
    private readonly List<(int, BiddingState)> _bidding2States = [];
    public readonly List<GameResult> GameResults = [];
    public readonly Lock readyLock = new();
    public readonly Lock joinLock = new();
}
