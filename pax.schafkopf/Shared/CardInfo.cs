using pax.schafkopf.lib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace pax.schafkopf.Shared
{
    public class CardInfo
    {
        public int pos { get; set; }
        public int rank { get; set; }
        public int suit { get; set; }

        public CardInfo() { }

        public CardInfo(SKCard card, int pos)
        {
            this.pos = pos;
            rank = card.Rank;
            suit = card.Suit;
        }
    }
}
