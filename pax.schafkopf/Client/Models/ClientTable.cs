using pax.schafkopf.lib;
using pax.schafkopf.lib.Enums;
using pax.schafkopf.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace pax.schafkopf.Client.Models
{
    public class ClientTable
    {
        public int ClientPosition { get; private set; }
        public ClientPlayer[] Players { get; set; } =
            new ClientPlayer[4] { new ClientPlayer() { Position = 0, TablePosition = 0 },
                new ClientPlayer() { Position = 1, TablePosition = 1 },
                new ClientPlayer() { Position = 2, TablePosition = 2 },
                new ClientPlayer() { Position = 3, TablePosition = 3 } 
            };
        public SKTable Table { get; set; }
        public bool isFull => !Players.Any(x => String.IsNullOrEmpty(x.Name));
        public ClientPlayer ClientPlayer => Players.Single(s => s.Position == ClientPosition);
        public bool hasDisconnects { get; set; } = false;
        public bool isVisitor { get; set; } = false;
        public int Visitors { get; set; }
        public bool isLoading { get; set; } = false;
        public string Error { get; set; } = String.Empty;
        public ClientTrick Trick { get; set; } = new ClientTrick();
        public AnimationMode AnimationMode { get; set; } = AnimationMode.Fast;
        public bool isLastTrick { get; set; } = false;
        public void SetClientPosition(int pos)
        {
            ClientPosition = pos;
            Players[ClientPosition].TablePosition = 0;
            for (int i = 1; i < 4; i++)
            {
                int nextpos = (ClientPosition + i) % 4;
                Players[nextpos].TablePosition = i;
            }
        }

        public void PlayCard(CardInfo card)
        {
            Table.PlayCard(card.rank, card.suit);
        }
    }
}
