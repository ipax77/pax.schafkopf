
namespace sk.shared;

public static class CardExtensions
{
    public static int GetValue(this Card card)
    {
        return card.Rank switch
        {
            Rank.Ace => 11,
            Rank.Ten => 10,
            Rank.King => 4,
            Rank.Ober => 3,
            Rank.Unter => 2,
            _ => 0
        };
    }

    public static int GetCardRank(this Card card, GameType mode, Suit trump, Card? firstCard = null)
    {
        if (mode == GameType.Ruf)
        {
            trump = Suit.Herz;
        }
        return mode switch
        {
            GameType.Wenz => card.Rank switch
            {
                Rank.Unter => 200 + (int)card.Suit,
                _ => firstCard == null ? (int)card.Rank : (int)firstCard.Suit == (int)card.Suit ? (int)card.Rank : 0
            },
            _ => card.Rank switch
            {
                Rank.Ober => 300 + (int)card.Suit,
                Rank.Unter => 200 + (int)card.Suit,
                _ => (card.Suit == trump) switch
                {
                    true => 100 + (int)card.Suit + (int)card.Rank,
                    false => firstCard == null ? (int)card.Rank : (int)firstCard.Suit == (int)card.Suit ? (int)card.Rank : 0
                }
            }
        };
    }

    public static int GetCardOrder(this Card card, GameType mode, Suit trump)
    {
        if (mode == GameType.Ruf)
        {
            trump = Suit.Herz;
        }

        return mode switch
        {
            GameType.Wenz => card.Rank switch
            {
                Rank.Unter => 200 + (int)card.Suit,
                _ => (int)card.Suit * 8 + (int)card.Rank
            },
            _ => card.Rank switch
            {
                Rank.Ober => 300 + (int)card.Suit,
                Rank.Unter => 200 + (int)card.Suit,
                _ => (card.Suit == trump) switch
                {
                    true => 100 + (int)card.Suit + (int)card.Rank,
                    false => (int)card.Suit * 8 + (int)card.Rank
                }
            }
        };
    }

    public static bool IsTrump(this Card card, GameType mode, Suit trump)
    {
        if (mode == GameType.Ruf)
        {
            trump = Suit.Herz;
        }

        return mode switch
        {
            GameType.Wenz => card.Rank == Rank.Unter,
            GameType.Solo => card.Rank == Rank.Ober || card.Rank == Rank.Unter || card.Suit == trump,
            _ => card.Rank == Rank.Ober || card.Rank == Rank.Unter || card.Suit == Suit.Herz,
        };
    }

    public static bool CanOperate(this Card card, Card firstCard, GameType mode, Suit trump)
    {
        if (mode == GameType.Ruf)
        {
            trump = Suit.Herz;
        }

        if (firstCard.IsTrump(mode, trump))
            return card.IsTrump(mode, trump);

        if (!card.IsTrump(mode, trump))
            return card.Suit == firstCard.Suit;

        return true;
    }

    public static string GetCssClass(this Card card)
    {
        if (card.Suit == Suit.None)
        {
            return "card-hidden";
        }
        var suit = card.Suit.ToString();
        var rank = card.Rank.ToString();
        return $"card-{rank}-{suit}";
    }
}