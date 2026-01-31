using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using pax.schafkopf.Client.Models;
using pax.schafkopf.lib;
using pax.schafkopf.lib.Enums;
using pax.schafkopf.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace pax.schafkopf.Client.Services
{
    public class HubService
    {
        private HubConnection hubConnection;
        public bool isserverConnected => hubConnection.State == HubConnectionState.Connected;
        private readonly NavigationManager NavigationManager;
        private readonly ILogger logger;
        Action Update;
        ClientTable table;
        string Name = String.Empty;
        string AuthName = String.Empty;
        Guid Guid = new Guid();
        private HttpClient httpClient;

        public HubService(NavigationManager navigationManager, ILogger logger, ClientTable table, Action Update, HttpClient httpClient)
        {
            NavigationManager = navigationManager;
            this.logger = logger;
            NavigationManager = navigationManager;
            this.table = table;
            this.Update = Update;
            this.httpClient = httpClient;

            hubConnection = new HubConnectionBuilder()
                .WithUrl(NavigationManager.ToAbsoluteUri("/skhub"))
                .WithAutomaticReconnect()
                .Build();

            hubConnection.On<PlayerInfo>("Joined", (player) =>
            {
                logger.LogInformation($"player joined the table ({Name}): {player.Name}|{player.Pos}");
                table.Players[player.Pos].Name = player.Name;
                Update();
            });

            hubConnection.On<int>("Disconnected", (player) =>
            {
                logger.LogInformation($"player disconnected ({Name}): {player}");
                table.hasDisconnects = true;
                table.Players[player].isDisconnected = true;
                Update();
            });

            hubConnection.On<int>("Reconnected", (player) =>
            {
                logger.LogInformation($"player reconnected ({Name}): {player}");
                table.Players[player].isDisconnected = false;
                if (!table.Players.Where(x => x.isDisconnected).Any())
                {
                    table.hasDisconnects = false;
                    Update();
                }
                logger.LogInformation($"player reconnected ({Name}): {player} {table.hasDisconnects}");
            });

            hubConnection.On<HandInfo>("Cards", (hand) =>
            {
                logger.LogInformation($"({Name}) got cards");
                table.isLastTrick = false;
                table.Table.NextRound();
                table.Table.Players[hand.pos].Cards = hand.cards.ToList();
                Update();
            });

            hubConnection.On<Bid1Info>("Bid1Info", (bidinfo) =>
            {
                logger.LogInformation($"player bid1 ({Name}): {bidinfo.pos}");
                if (table.Table.CurrentPlayer != bidinfo.pos)
                    logger.LogError($"failed bidding1: {table.Table.CurrentPlayer} <=> {bidinfo.pos}");
                table.Table.Bidding1(bidinfo.playing);
                if (table.Table.State == (int)lib.Enums.TableState.Finished)
                    table.isLastTrick = true;
                Update();
            });

            hubConnection.On<Bid2Info>("Bid2Info", (bidinfo) =>
            {
                logger.LogInformation($"player bid1 ({Name}): {bidinfo.pos}");
                if (table.Table.CurrentPlayer != bidinfo.pos)
                    logger.LogError($"failed bidding2: {table.Table.CurrentPlayer} <=> {bidinfo.pos}");
                table.Table.Bidding2(bidinfo.mode, bidinfo.trump, bidinfo.partner);
                Update();
            });

            hubConnection.On<CardInfo>("Card", async (cardinfo) =>
            {
                logger.LogInformation($"player bid1 ({Name}): {cardinfo.pos}");
                if (table.Table.CurrentPlayer != cardinfo.pos)
                    logger.LogError($"failed bidding2: {table.Table.CurrentPlayer} <=> {cardinfo.pos}");
                table.Trick.TrickCard = cardinfo;
                logger.LogInformation($"{Name} got card {table.Table.TrickCount}");
                table.Table.PlayCard(cardinfo.rank, cardinfo.suit);
                Update();
                if (table.Table.State == (int)lib.Enums.TableState.Finished)
                {
                    await Task.Delay(SKData.AnimationTime(table.AnimationMode) - 25 + 1750);
                    table.isLastTrick = true;
                    Update();
                }
                logger.LogInformation($"{Name} card played {table.Table.TrickCount}");
            });

            hubConnection.On<bool>("Visitor", (isVisiting) =>
            {
                if (isVisiting)
                    table.Visitors++;
                else
                    table.Visitors--;
                Update();
            });
        }

        public async Task<bool> Connect(string name, string authname)
        {
            Name = name;
            AuthName = authname;
            try
            {
                await hubConnection.StartAsync();
                await hubConnection.InvokeAsync("Connect", Name, AuthName);
                return true;
            }
            catch (Exception e)
            {
                logger.LogError(e.Message);
                return false;
            }
        }

        public async Task<bool> StartTable()
        {
            if (isserverConnected)
            {
                TableStartInfo info;
                try
                {
                    table.Table = new SKTable(true);
                    info = await hubConnection.InvokeAsync<TableStartInfo>("StartTable");
                }
                catch (Exception e)
                {
                    logger.LogError(e.Message);
                    return false;
                }
                table.SetClientPosition(0);
                table.Table.InitClient(new SKConfig(), info.guid, info.leader);
                table.ClientPlayer.Name = Name;
                Guid = info.guid;
                return true;
            }
            else
                return false;
        }

        public async Task<bool> JoinTable(Guid guid)
        {
            if (isserverConnected)
            {
                TableJoinInfo info;
                try
                {
                    table.Table = new SKTable(true);
                    info = await hubConnection.InvokeAsync<TableJoinInfo>("JoinTable", guid);
                }
                catch (Exception e)
                {
                    logger.LogError(e.Message);
                    return false;
                }
                table.Table.InitClient(new SKConfig(), guid, info.leader);
                table.SetClientPosition(info.myPos);
                foreach (var pl in info.playerInfos)
                    table.Players[pl.Pos].Name = pl.Name;
                table.ClientPlayer.Name = Name;
                Guid = guid;
                return true;
            }
            else
                return false;
        }

        public async Task<bool> VisitTable(Guid guid)
        {
            if (isserverConnected)
            {
                TableInfo info;
                try
                {
                    info = await hubConnection.InvokeAsync<TableInfo>("VisitTable", guid);
                }
                catch (Exception e)
                {
                    logger.LogError(e.Message);
                    return false;
                }
                table.Table = info.Table;
                table.Table.Init();
                table.Table.isClient = true;
                table.SetClientPosition(0);
                foreach (var pl in info.playerInfos)
                    table.Players[pl.Pos].Name = pl.Name;
                table.isVisitor = true;
                return true;
            }
            else
                return false;
        }

        public async Task<bool> ReconnectTable(Guid guid)
        {
            bool success = false;
            if (!isserverConnected)
                await Connect(Name, AuthName);

            if (isserverConnected)
            {
                TableInfo info = new TableInfo();
                try
                {
                    // info = await httpClient.GetFromJsonAsync<TableInfo>($"Schafkopf/{guid}/{Name}/{AuthName}/{hubConnection.ConnectionId}");
                    info = await hubConnection.InvokeAsync<TableInfo>("ReconnectTable", guid);
                }
                catch (Exception e)
                {
                    logger.LogError(e.Message);
                    return false;
                }
                if (info == null)
                    return false;
                table.Table = info.Table;
                table.Table.Init();
                table.Table.isClient = true;
                table.SetClientPosition(info.myPosition);
                foreach (var pl in info.playerInfos)
                    table.Players[pl.Pos].Name = pl.Name;
                if (table.Table.State == (int)lib.Enums.TableState.Finished)
                    table.isLastTrick = true;
                return true;
            }
            else
                return false;
        }

        public async Task Bidding1(bool playing)
        {
            bool success = false;
            try
            {
                success = await hubConnection.InvokeAsync<bool>("Bidding1", playing);
                table.Table.Bidding1(playing);
                if (table.Table.State == (int)lib.Enums.TableState.Finished)
                    table.isLastTrick = true;
            } catch (Exception e)
            {
                logger.LogError($"{Name}: failed bidding1 {e.Message}");

            }
            if (!success)
                await ReconnectTable(Guid);
        }

        public async Task Bidding2(int mode, int trump, int partner)
        {
            bool success = false;
            try
            {
                success = await hubConnection.InvokeAsync<bool>("Bidding2", mode, trump, partner);
                table.Table.Bidding2(mode, trump, partner);
            } catch (Exception e)
            {
                logger.LogError($"{Name}: failed bidding2 {e.Message}");
            }
            //if (!success)
            //    await ReconnectTable(Guid);
        }

        public async Task PlayCard(int rank, int suit)
        {
            bool success = false;
            try
            {
                success = await hubConnection.InvokeAsync<bool>("PlayCard", rank, suit);
                table.Table.PlayCard(rank, suit);
                if (table.Table.State == (int)lib.Enums.TableState.Finished)
                {
                    await Task.Delay(SKData.AnimationTime(table.AnimationMode) - 25 + 1750);
                    table.isLastTrick = true;
                    Update();
                }
            }
            catch (Exception e)
            {
                logger.LogError(e.Message);
            }
            if (!success)
                await ReconnectTable(Guid);
        }

        public async Task NextRound()
        {
            bool success = false;
            try
            {
                success = await hubConnection.InvokeAsync<bool>("NextRound");
            }
            catch (Exception e)
            {
                logger.LogError(e.Message);
            }
            if (!success)
                await ReconnectTable(Guid);
        }

    }
}
