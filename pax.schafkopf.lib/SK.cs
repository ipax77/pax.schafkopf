using System;
using System.Collections.Generic;
using System.Text;

namespace pax.schafkopf.lib
{
    public static class SK
    {
        private static Dictionary<Guid, SKTable> Tables = new Dictionary<Guid, SKTable>();
        public static SKCard[] Deck; 

        public static void AddTable(SKTable table)
        {
            if (Deck == null)
            {
                Deck = new SKCard[32];
                int i = 0;
                for (byte r = 0; r < 8; r++)
                    for (byte s = 0; s < 4; s++)
                    {
                        Deck[i] = new SKCard() { Rank = r, Suit = s };
                        i++;
                    }
            }
            Tables[table.Guid] = table;
        }

        public static int PlayerDistance(int leader, int pos)
        {
            int dist = 0;
            while (leader != pos)
            {
                leader = (leader + 1) % 4;
                dist++;
            }
            return dist;
        }
    }
}
