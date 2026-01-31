using pax.schafkopf.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace pax.schafkopf.Server.Models
{
    public class ServerPlayer
    {
        public int ID { get; set; }
        public string Name { get; set; }
        public string AuthName { get; set; }
        public string ConnectionID { get; set; }
        public int Position { get; set; }
        public bool isDisconnected { get; set; } = false;
        public bool NextRound { get; set; } = false;

        public PlayerInfo GetPlayerInfo()
        {
            return new PlayerInfo()
            {
                Name = Name,
                Pos = Position
            };
        }
    }
}
