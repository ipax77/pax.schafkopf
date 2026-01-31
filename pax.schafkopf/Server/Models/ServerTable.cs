using pax.schafkopf.lib;
using pax.schafkopf.Shared;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace pax.schafkopf.Server.Models
{
    public class ServerTable
    {
        public int ID { get; set; }
        public ICollection<ServerPlayer> Players { get; set; }
        public ICollection<ServerPlayer> Visitors { get; set; }
        [NotMapped]
        public SKTable Table { get; set; }
        [NotMapped]
        public object lockobject = new object();
        [NotMapped]
        public bool hasDisconnects => Players.Where(x => x.isDisconnected).Any();

        public ServerTable()
        {
            Players = new HashSet<ServerPlayer>();
            Visitors = new HashSet<ServerPlayer>();
        }

        public IEnumerable<string> ConnectionIDs(string excludeid = "")
        {
            var ids = Players.Select(s => s.ConnectionID).ToList();
            ids.AddRange(Visitors.Select(s => s.ConnectionID));
            if (!String.IsNullOrEmpty(excludeid))
                ids.Remove(excludeid);
            return ids;
        }

        public TableInfo GetTableInfo(int pos = -1)
        {
            var info = new TableInfo()
            {
                playerInfos = Players.Select(s => s.GetPlayerInfo()).ToList(),
                myPosition = pos,
                Visitors = Visitors.Count()
            };
            info.Table = JsonSerializer.Deserialize<SKTable>(JsonSerializer.Serialize(Table));
            foreach (var pl in info.Table.Players.Where(x => x.Position != pos))
                pl.Cards = new List<SKCard>();

            return info;
        }
    }
}

