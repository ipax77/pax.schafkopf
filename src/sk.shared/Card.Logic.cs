namespace sk.shared;

public partial record Card
{
    public int GetValue()
    {
        return Rank switch
        {
            Rank.Ace => 11,
            Rank.Ten => 10,
            Rank.King => 4,
            Rank.Ober => 3,
            Rank.Unter => 2,
            _ => 0
        };
    }

    public int GetCardRank(GameType mode, Suit trump, Card? firstCard = null)
    {
        if (mode == GameType.Ruf)
        {
            trump = Suit.Herz;
        }
        return mode switch
        {
            GameType.Wenz => Rank switch
            {
                Rank.Unter => 200 + (int)Suit,
                _ => firstCard == null ? (int)Rank : (int)firstCard.Suit == (int)Suit ? (int)Rank : 0
            },
            _ => Rank switch
            {
                Rank.Ober => 300 + (int)Suit,
                Rank.Unter => 200 + (int)Suit,
                _ => (Suit == trump) switch
                {
                    true => 100 + (int)Suit + (int)Rank,
                    false => firstCard == null ? (int)Rank : (int)firstCard.Suit == (int)Suit ? (int)Rank : 0
                }
            }
        };
    }

    public int GetCardOrder(GameType mode, Suit trump)
    {
        if (mode == GameType.Ruf)
        {
            trump = Suit.Herz;
        }

        return mode switch
        {
            GameType.Wenz => Rank switch
            {
                Rank.Unter => 200 + (int)Suit,
                _ => (int)Suit * 8 + (int)Rank
            },
            _ => Rank switch
            {
                Rank.Ober => 300 + (int)Suit,
                Rank.Unter => 200 + (int)Suit,
                _ => (Suit == trump) switch
                {
                    true => 100 + (int)Suit + (int)Rank,
                    false => (int)Suit * 8 + (int)Rank
                }
            }
        };
    }

    public bool IsTrump(GameType mode, Suit trump)
    {
        if (mode == GameType.Ruf)
        {
            trump = Suit.Herz;
        }

        return mode switch
        {
            GameType.Wenz => Rank == Rank.Unter,
            GameType.Solo => Rank == Rank.Ober || Rank == Rank.Unter || Suit == trump,
            _ => Rank == Rank.Ober || Rank == Rank.Unter || Suit == Suit.Herz,
        };
    }

    public bool CanOperate(Card firstCard, GameType mode, Suit trump, List<Card> hand)
    {
        if (mode == GameType.Ruf)
        {
            trump = Suit.Herz;
        }

        if (firstCard.IsTrump(mode, trump))
        {
            if (!hand.Any(a => a.IsTrump(mode, trump)))
            {
                return true;
            }
            return IsTrump(mode, trump);
        }

        if (!IsTrump(mode, trump))
        {
            if (!hand.Any(a => !a.IsTrump(mode, trump) && a.Suit == firstCard.Suit))
            {
                return true;
            }
            return Suit == firstCard.Suit;
        }

        return true;
    }

    public string GetCssClass()
    {
        if (Suit == Suit.None)
        {
            return "card-hidden";
        }
        var suit = Suit.ToString();
        var rank = Rank.ToString();
        return $"card-{rank}-{suit}";
    }

    public string GetElementId()
    {
        return $"trickcard{Suit}{Rank}";
    }
}
