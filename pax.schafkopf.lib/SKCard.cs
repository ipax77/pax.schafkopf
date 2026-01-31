using pax.schafkopf.lib.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace pax.schafkopf.lib
{
    public class SKCard
    {
        public byte Rank { get; set; }
        public byte Suit { get; set; }

        public SKCard Copy()
        {
            return new SKCard() { Rank = Rank, Suit = Suit };
        }

        public int GetValue()
        {
            return Rank switch
            {
                (int)Enums.Rank.Ace => 11,
                (int)Enums.Rank.Ten => 10,
                (int)Enums.Rank.King => 4,
                (int)Enums.Rank.Ober => 3,
                (int)Enums.Rank.Unter => 2,
                _ => 0
            };
        }

        public int GetCardRank(int mode, int trump, SKCard firstCard = null)
        {
            return mode switch
            {
                (int)GameMode.Wenz => Rank switch
                {
                    (int)Enums.Rank.Unter => 200 + Suit,
                    _ => firstCard == null ? Rank : firstCard.Suit == Suit ? Rank : 0
                },
                _ => Rank switch
                {
                    (int)Enums.Rank.Ober => 300 + Suit,
                    (int)Enums.Rank.Unter => 200 + Suit,
                    _ => (Suit == trump) switch
                    {
                        true => 100 + Suit + Rank,
                        false => firstCard == null ? Rank : firstCard.Suit == Suit ? Rank : 0
                    }
                }
            };
        }

        public int GetCardOrder(int mode, int trump)
        {
            return mode switch
            {
                (int)GameMode.Wenz => Rank switch
                {
                    (int)Enums.Rank.Unter => 200 + Suit,
                    _ => Suit * 8 + Rank
                },
                _ => Rank switch
                {
                    (int)Enums.Rank.Ober => 300 + Suit,
                    (int)Enums.Rank.Unter => 200 + Suit,
                    _ => (Suit == trump) switch
                    {
                        true => 100 + Suit + Rank,
                        false => Suit * 8 + Rank
                    }
                }
            };
        }

        public bool isTrump(GameMode mode, Suit trump)
        {
            return mode switch
            {
                Enums.GameMode.Ruf => this.Rank == (byte)Enums.Rank.Ober || this.Rank == (byte)Enums.Rank.Unter || this.Suit == (byte)Enums.Suit.Herz,
                Enums.GameMode.Wenz => this.Rank == (byte)Enums.Rank.Unter,
                Enums.GameMode.WenzTout => this.Rank == (byte)Enums.Rank.Unter,
                Enums.GameMode.Solo => this.Rank == (byte)Enums.Rank.Ober || this.Rank == (byte)Enums.Rank.Unter || this.Suit == (byte)trump,
                Enums.GameMode.SoloTout => this.Rank == (byte)Enums.Rank.Ober || this.Rank == (byte)Enums.Rank.Unter || this.Suit == (byte)trump,
                Enums.GameMode.Sie => this.Rank == (byte)Enums.Rank.Ober || this.Rank == (byte)Enums.Rank.Unter,
                _ => this.Rank == (byte)Enums.Rank.Ober || this.Rank == (byte)Enums.Rank.Unter || this.Suit == (byte)Enums.Suit.Herz,
            };
        }

        public bool canOperate(SKCard card, GameMode mode, Suit trump)
        {
            return card.isTrump(mode, trump) && this.isTrump(mode, trump)
                || !card.isTrump(mode, trump) &&
                    (!this.isTrump(mode, trump) && card.Suit == this.Suit);
        }
    }
}
