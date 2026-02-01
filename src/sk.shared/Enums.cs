namespace sk.shared;

public enum GameType
{
    None = 0,
    Ruf = 1,
    Wenz = 2,
    Solo = 3,
    Ramsch = 5,
}

public enum Suit
{
    None = 0,
    Schellen = 1,
    Herz = 2,
    Gras = 3,
    Eichel = 4
}

public enum Rank
{
    Seven = 1,
    Eight = 2,
    Nine = 3,
    Unter = 4,
    Ober = 5,
    King = 6,
    Ten = 7,
    Ace = 8
}

public enum GameState
{
    None = 0,
    Bidding1 = 1,
    Bidding2 = 2,
    Playing = 3,
    Finished = 4
}

public enum Bidding1Bid
{
    None = 0,
    WouldPlay = 1,
    WouldPlayToo = 2
}

[Flags]
public enum GameModifier
{
    None = 0,
    Sie = 1,
    Tout = 2,
    Contra = 3,
    Re = 4
}