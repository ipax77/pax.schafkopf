using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using pax.schafkopf.lib;
using pax.schafkopf.Server.Models;
using pax.schafkopf.Server.Services;
using pax.schafkopf.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace pax.schafkopf.Server.Hubs
{
    public class SchafkopfHub : Hub
    {
        private readonly ILogger<SchafkopfHub> logger;

        public SchafkopfHub(ILogger<SchafkopfHub> logger)
        {
            this.logger = logger;
        }

        public override async Task OnConnectedAsync()
        {
            logger.LogInformation($"user connected: {Context.ConnectionId}|{Context.UserIdentifier}");
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception e)
        {
            logger.LogInformation($"user disconnected: {Context.ConnectionId}");
            if (!Context.Items.ContainsKey("guid"))
                return;
            Guid guid = (Guid)Context.Items["guid"];
            ServerTable table = TableService.Tables[guid];
            ServerPlayer player = table.Players.Single(s => s.Name == (string)Context.Items["name"] && s.AuthName == (string)Context.Items["authname"]);
            player.isDisconnected = true;
            _ = Notify("Disconnected", table.ConnectionIDs(Context.ConnectionId), (int)Context.Items["pos"]);
            Context.Items.Clear();
            await base.OnDisconnectedAsync(e);
        }

        public async Task Notify(string note, IEnumerable<string> ids, object send)
        {
            foreach (var id in ids)
                if (!String.IsNullOrEmpty(id))
                    await Clients.Client(id).SendAsync(note, send);
        }

        public void Connect(string Name, string AuthName)
        {
            logger.LogInformation($"player connected: {Name}");
            Context.Items.Clear();
            Context.Items.Add("name", Name);
            Context.Items.Add("authname", AuthName);
        }

        public TableStartInfo StartTable()
        {
            ServerTable table = new ServerTable()
            {
                Table = new SKTable(false)
            };
            table.Table.Init();
            ServerPlayer player = new ServerPlayer()
            {
                Name = (string)Context.Items["name"],
                AuthName = (string)Context.Items["authname"],
                Position = 0,
                ConnectionID = Context.ConnectionId
            };
            table.Players.Add(player);
            Context.Items.Add("guid", table.Table.Guid);
            Context.Items.Add("pos", player.Position);
            TableService.Tables.TryAdd(table.Table.Guid, table);
            return new TableStartInfo()
            {
                leader = table.Table.StartingPlayer,
                guid = table.Table.Guid
            };
        }

        public TableJoinInfo JoinTable(Guid guid)
        {
            if (!TableService.Tables.ContainsKey(guid))
            {
                return null;
            }
            else
            {
                ServerTable table = TableService.Tables[guid];
                lock (table.lockobject)
                {
                    if (table.Players.Count() >= 4)
                        return null;
                    else
                    {
                        ServerPlayer player = new ServerPlayer()
                        {
                            Name = (string)Context.Items["name"],
                            AuthName = (string)Context.Items["authname"],
                            Position = table.Players.Count(),
                            ConnectionID = Context.ConnectionId
                        };
                        TableJoinInfo info = new TableJoinInfo()
                        {
                            playerInfos = table.Players.Select(s => s.GetPlayerInfo()).ToList(),
                            myPos = player.Position,
                            leader = table.Table.StartingPlayer
                        };
                        table.Players.Add(player);
                        Context.Items.Add("guid", table.Table.Guid);
                        Context.Items.Add("pos", player.Position);
                        PlayerInfo joinInfo = player.GetPlayerInfo();
                        _ = Notify("Joined", table.ConnectionIDs(Context.ConnectionId), joinInfo);
                        if (table.Players.Count() == 4)
                        {
                            table.Table.NextRound();
                            _ = DealCards(table);
                        }
                        return info;
                    }
                }
            }
        }

        public TableInfo VisitTable(Guid guid)
        {
            if (!TableService.Tables.ContainsKey(guid))
            {
                return null;
            }
            else
            {
                ServerTable table = TableService.Tables[guid];
                ServerPlayer player = new ServerPlayer()
                {
                    Name = (string)Context.Items["name"],
                    AuthName = (string)Context.Items["authname"],
                    Position = -1,
                    ConnectionID = Context.ConnectionId
                };
                table.Visitors.Add(player);
                Context.Items.Add("guid", table.Table.Guid);
                Context.Items.Add("pos", player.Position);
                _ = Notify("Visitor", table.ConnectionIDs(Context.ConnectionId), true);
                return table.GetTableInfo();
            }
        }

        public TableInfo ReconnectTable(Guid guid)
        {
            if (!TableService.Tables.ContainsKey(guid))
            {
                return null;
            }
            else
            {
                ServerTable table = TableService.Tables[guid];
                ServerPlayer player = table.Players.SingleOrDefault(s => s.Name == (string)Context.Items["name"] && s.AuthName == (string)Context.Items["authname"]);
                if (player == null)
                    return null;
                player.isDisconnected = false;
                player.ConnectionID = Context.ConnectionId;
                if (!Context.Items.ContainsKey("guid"))
                    Context.Items.Add("guid", table.Table.Guid);
                else
                    Context.Items["guid"] = table.Table.Guid;
                if (!Context.Items.ContainsKey("pos"))
                    Context.Items.Add("pos", player.Position);
                else
                    Context.Items["pos"] = player.Position;
                _ = Notify("Reconnected", table.ConnectionIDs(Context.ConnectionId), player.Position);
                var info = table.GetTableInfo(player.Position);
                return info;
            }

        }

        public void LeaveTable()
        {
            if (!Context.Items.ContainsKey("guid"))
                return;
            Guid guid = (Guid)Context.Items["guid"];
            int pos = (int)Context.Items["pos"];
            ServerTable table = TableService.Tables[guid];
            ServerPlayer player = table.Players.Single(s => s.Name == (string)Context.Items["name"] && s.AuthName == (string)Context.Items["authname"]);
            if (pos >= 0)
            {
                table.Players.Remove(player);
                _ = Notify("Left", table.ConnectionIDs(Context.ConnectionId), player.Position);
            }
            else
            {
                table.Visitors.Remove(player);
                _ = Notify("Visitor", table.ConnectionIDs(Context.ConnectionId), false);
            }
        }

        public bool Bidding1(bool playing)
        {
            if (!Context.Items.ContainsKey("guid"))
                return false;
            Guid guid = (Guid)Context.Items["guid"];
            int pos = (int)Context.Items["pos"];
            ServerTable table = TableService.Tables[guid];
            if (table.Table.CurrentPlayer != pos)
            {
                logger.LogError($"failed bidding1: {table.Table.CurrentPlayer} <=> {pos}");
                //return false;
            }
            table.Table.Bidding1(playing);
            Bid1Info info = new Bid1Info() {
                pos = pos,
                playing = playing
            };
            _ = Notify("Bid1Info", table.ConnectionIDs(Context.ConnectionId), info);
            return true;
        }

        public bool Bidding2(int mode, int trump, int partner)
        {
            if (!Context.Items.ContainsKey("guid"))
                return false;
            Guid guid = (Guid)Context.Items["guid"];
            int pos = (int)Context.Items["pos"];
            ServerTable table = TableService.Tables[guid];
            if (table.Table.CurrentPlayer != pos)
            {
                logger.LogError($"failed bidding2: {table.Table.CurrentPlayer} <=> {pos}");
                // return false;
            }
            table.Table.Bidding2(mode, trump, partner);
            Bid2Info info = new Bid2Info()
            {
                pos = pos,
                mode = mode,
                trump = trump,
                partner = partner
            };
            _ = Notify("Bid2Info", table.ConnectionIDs(Context.ConnectionId), info);
            return true;
        }

        public bool PlayCard(int rank, int suit)
        {
            if (!Context.Items.ContainsKey("guid"))
                return false;
            Guid guid = (Guid)Context.Items["guid"];
            int pos = (int)Context.Items["pos"];
            ServerTable table = TableService.Tables[guid];
            if (table.Table.CurrentPlayer != pos)
            {
                logger.LogError($"failed playcard: {table.Table.CurrentPlayer} <=> {pos}");
                return false;
            }
            table.Table.PlayCard(rank, suit);
            CardInfo info = new CardInfo()
            {
                pos = pos,
                rank = rank,
                suit = suit
            };
            logger.LogInformation($"Round: {table.Table.TrickCount}");
            _ = Notify("Card", table.ConnectionIDs(Context.ConnectionId), info);
            return true;
        }

        public bool NextRound()
        {
            if (!Context.Items.ContainsKey("guid"))
                return false;
            Guid guid = (Guid)Context.Items["guid"];
            int pos = (int)Context.Items["pos"];
            ServerTable table = TableService.Tables[guid];
            ServerPlayer player = table.Players.SingleOrDefault(s => s.Name == (string)Context.Items["name"] && s.AuthName == (string)Context.Items["authname"]);
            if (player == null)
                return false;
            player.NextRound = true;
            if (table.Players.Where(x => x.NextRound).Count() == 4)
            {
                string result = "";
                foreach (var pl in table.Table.Players.OrderByDescending(o => o.Points))
                    result += $"{pl.Position} => {pl.Points}; ";
                logger.LogInformation(result);
                table.Table.NextRound();
                foreach (var pl in table.Players)
                    pl.NextRound = false;
                _ = DealCards(table);
            }
            return true;
        }

        public async Task DealCards(ServerTable table)
        {
            for (int i = 0; i < 4; i++)
                await Clients.Client(table.Players.Single(s => s.Position == i).ConnectionID).SendAsync("Cards",
                    new HandInfo() { pos = i, cards = table.Table.Players[i].Cards });
        }

    }
}
