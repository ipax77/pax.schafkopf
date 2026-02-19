
namespace sk.shared;

public partial class Bidding2Result
{
    public int PlayerIndex { get; set; }
    public GameType GameType { get; set; }
    public Suit Suit { get; set; }
    public bool Sie { get; set; }
    public bool Tout { get; set; }

    public string GetGameTypeString()
    {
        var gameTypeString = GameType switch
        {
            GameType.Sie => "Sie",
            GameType.Wenz => "Wenz",
            GameType.Ruf => Suit switch
            {
                Suit.Schellen => "Auf die Lumberte",
                Suit.Eichel => "Auf die Alte",
                Suit.Gras => "Auf die Blaue",
                _ => string.Empty
            },
            GameType.Solo => Suit switch
            {
                Suit.Schellen => "Schellen Solo",
                Suit.Eichel => "Eichel Solo",
                Suit.Gras => "Gras Solo",
                Suit.Herz => "Herz Solo",
                _ => string.Empty
            },
            _ => string.Empty
        };

        if (Tout)
        {
            gameTypeString += " Tout";
        }

        return gameTypeString;
    }

    public string GetTeammateString()
    {
        var teammateString = GameType switch
        {
            GameType.Ruf => Suit switch
            {
                Suit.Schellen => "Lumberte",
                Suit.Eichel => "Alte",
                Suit.Gras => "Blaue",
                _ => string.Empty
            },
            _ => string.Empty
        };
        return teammateString;
    }
}
