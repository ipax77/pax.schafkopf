using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace pax.schafkopf.lib
{
    public class SKPlayer
    {
        public byte Position { get; set; }
        public int Points { get; set; }
        public bool isPlaying { get; set; } = false;
        public bool isPartner { get; set; } = false;
        public byte GameProposal { get; set; }
        public byte TrumpProposal { get; set; }
        public byte Partner { get; set; }
        public bool isWinner { get; set; } = false;
        public bool isPlayer { get; set; } = false;
        public SKCard TrickCard { get; set; } = null;
        public List<SKCard> Cards { get; set; } = new List<SKCard>();
        public List<SKCard> CardsPlayed { get; set; } = new List<SKCard>();
        public List<SKCard> Tricks { get; set; } = new List<SKCard>();

        public void Reset()
        {
            isPlaying = false;
            isPartner = false;
            GameProposal = (byte)Enums.GameMode.Weiter;
            TrumpProposal = (byte)Enums.Suit.Farblos;
            Partner = (byte)Enums.Suit.Farblos;
            isWinner = false;
            isPlayer = false;
            TrickCard = null;
            Tricks = new List<SKCard>();
            CardsPlayed = new List<SKCard>();
        }

        public IEnumerable<SKCard> ValidCards(SKCard firstCard, SKTable table)
        {
            IEnumerable<SKCard> ValidCards;
            if (firstCard == null)
            {
                if ((table.PlayingPlayer >= 0 && table.GameMode == (byte)Enums.GameMode.Ruf && !table.Players.Where(x => x.isPartner).Any()) // Ruf und partner noch nicht gefunden
                    && (Cards.FirstOrDefault(f => f.Rank == (byte)Enums.Rank.Ace && f.Suit == table.Players[table.PlayingPlayer].Partner) != null) // hat die Rufsau
                    && (Cards.Where(x => x.Rank != (byte)Enums.Rank.Ober && x.Rank != (byte)Enums.Rank.Unter && x.Suit == table.Players[table.PlayingPlayer].Partner).Count() < 4) // 'drunter durch'
                )
                {
                    var invalids = Cards.Where(x => x.Rank != (byte)Enums.Rank.Ober && x.Rank != (byte)Enums.Rank.Unter && x.Rank != (byte)Enums.Rank.Ace && x.Suit == table.Players[table.PlayingPlayer].Partner);
                    ValidCards = Cards.Except(invalids);
                }
                else
                    ValidCards = Cards;
            }
            else
            {
                if ((table.PlayingPlayer >= 0 && table.GameMode == (byte)Enums.GameMode.Ruf && !table.Players.Where(x => x.isPartner).Any()) // Ruf und partner noch nicht gefunden
                    && (firstCard.Rank != (byte)Enums.Rank.Ober && firstCard.Rank != (byte)Enums.Rank.Unter && firstCard.Rank != (byte)Enums.Rank.Ace && firstCard.Suit == table.Players[table.PlayingPlayer].Partner) // gerufen
                    && (Cards.FirstOrDefault(f => f.Rank == (byte)Enums.Rank.Ace && f.Suit == table.Players[table.PlayingPlayer].Partner) != null) // hat die Rufsau
                )
                {
                    ValidCards = new List<SKCard>() { Cards.First(f => f.Rank == (byte)Enums.Rank.Ace && f.Suit == table.Players[table.PlayingPlayer].Partner) };
                }
                else if ((table.PlayingPlayer >= 0 && table.GameMode == (byte)Enums.GameMode.Ruf && !table.Players.Where(x => x.isPartner).Any() && Cards.Count > table.Config.MaxCardsForRufSauSchmieren) // Ruf und partner noch nicht gefunden
                    && !Cards.Where(x => x.canOperate(firstCard, (Enums.GameMode)table.GameMode, (Enums.Suit)table.Trump)).Any() // gespielte Karte kann nicht bedient werden
                    && (Cards.FirstOrDefault(f => f.Rank == (byte)Enums.Rank.Ace && f.Suit == table.Players[table.PlayingPlayer].Partner) != null) // hat die Rufsau
)
                {
                    var invalids = new List<SKCard>() { Cards.First(f => f.Rank == (byte)Enums.Rank.Ace && f.Suit == table.Players[table.PlayingPlayer].Partner) };
                    ValidCards = Cards.Except(invalids);
                }

                //else if (firstCard.Rank == (byte)Enums.Rank.Ober || firstCard.Rank == (byte)Enums.Rank.Unter || firstCard.Suit == (int)table.Trump)
                //    if (Cards.Where(x => x.Rank == (byte)Enums.Rank.Ober || x.Rank == (byte)Enums.Rank.Unter || x.Suit == (int)table.Trump).Any())
                //        ValidCards = Cards.Where(x => x.Rank == (byte)Enums.Rank.Ober || x.Rank == (byte)Enums.Rank.Unter || x.Suit == (int)table.Trump);
                //    else
                //        ValidCards = Cards;
                //else
                //    if (Cards.Where(x => x.Rank != (byte)Enums.Rank.Ober && x.Rank != (byte)Enums.Rank.Unter && x.Suit == firstCard.Suit).Any())
                //    ValidCards = Cards.Where(x => x.Rank != (byte)Enums.Rank.Ober && x.Rank != (byte)Enums.Rank.Unter && x.Suit == firstCard.Suit);
                //else
                //    ValidCards = Cards;
                else
                {
                    ValidCards = Cards.Where(x => x.canOperate(firstCard, (Enums.GameMode)table.GameMode, (Enums.Suit)table.Trump));
                    if (!ValidCards.Any())
                        ValidCards = Cards;
                }
            }
            return ValidCards;
        }
    }
}
