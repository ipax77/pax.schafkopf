using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using pax.schafkopf.Client.Models;
using pax.schafkopf.Client.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Blazored.LocalStorage;
using pax.schafkopf.lib;
using pax.schafkopf.Shared;
using pax.schafkopf.Client.Shared;
using pax.schafkopf.lib.Enums;
using System.Net.Http;

namespace pax.schafkopf.Client.Pages
{
    public partial class TablePage : ComponentBase
    {
        [Inject]
        protected ILogger<TablePage> logger { get; set; }
        [Inject]
        protected ILoggerFactory loggerFactory { get; set; }
        [Inject]
        protected NavigationManager navigationManager { get; set; }

        [Inject]
        protected IJSRuntime _js { get; set; }
        [Inject]
        protected ISyncLocalStorageService localStorage { get; set; }
        [Inject]
        protected HttpClient httpClient { get; set; }

        [Parameter]
        public string tableid { get; set; }
        [Parameter]
        public string debugname { get; set; }

        HubService hub;
        Action stateUpdate;
        ClientTable Table;
        ClientPlayer Player;
        string storedTableID = String.Empty;
        string animationClass = "btn-outline-secondary";
        LastTrickComponent LastTrick;
        bool showHands = false;

        protected override void OnInitialized()
        {
            Table = new ClientTable();
            Player = new ClientPlayer();
            stateUpdate = Update;
            if (!String.IsNullOrEmpty(debugname))
            {
                Player.Name = debugname;
                Player.AuthName = debugname + "123";

            } else
                (Player.Name, Player.AuthName) = StorageService.GetUserName(localStorage);
            if (!String.IsNullOrEmpty(tableid) && Guid.TryParse(tableid, out _))
                storedTableID = tableid;
            else
            {
                tableid = String.Empty;
                storedTableID = StorageService.GetTableID(localStorage);
            }
            base.OnInitialized();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
            {
                
            }
        }

        public async Task<bool> InitHub()
        {
            hub = new HubService(navigationManager, loggerFactory.CreateLogger("HubService"), Table, stateUpdate, httpClient);
            await _js.InvokeVoidAsync("ScreenSleep");
            return await hub.Connect(Player.Name, Player.AuthName);
        }

        void OnPlayerNameSet()
        {
            StorageService.SetUserName(localStorage, Player.Name, Player.AuthName);
            StateHasChanged();
        }

        public async Task ReconnectTable()
        {
            bool success = false;
            Table.isLoading = true;
            Guid guid;
            if (Guid.TryParse(storedTableID, out guid))
                if (await InitHub())
                    success = await hub.ReconnectTable(guid);
            Table.isLoading = false;
            StateHasChanged();
        }

        public async Task SetConfig()
        {

        }


        public async Task StartTable()
        {
            bool success = false;
            Table.isLoading = true;
            if (await InitHub())
                success = await hub.StartTable();
            if (success)
                StorageService.SetTableID(localStorage, Table.Table.Guid);
            Table.isLoading = false;
        }

        public async Task JoinTable(Guid guid)
        {
            bool success = false;
            Table.isLoading = true;
            if (await InitHub())
                success = await hub.JoinTable(guid);
            if (success)
                StorageService.SetTableID(localStorage, Table.Table.Guid);
            Table.isLoading = false;

        }

        public async Task VisitTable(Guid guid)
        {
            bool success = false;
            Table.isLoading = true;
            if (await InitHub())
                success = await hub.VisitTable(guid);
            Table.isLoading = false;
        }

        public void NewTable()
        {
            navigationManager.NavigateTo("/");
        }

        public async Task Bidding1(bool playing)
        {
            await hub.Bidding1(playing);
        }

        public async Task Bidding2()
        {
            int mode = Table.ClientPlayer.GameMode;
            int trump = Table.ClientPlayer.GameSuit;
            int partner = Table.ClientPlayer.GameSuit;
            if (mode == (int)GameMode.Ruf)
                trump = (int)Suit.Herz;
            await hub.Bidding2(mode, trump, partner);
        }

        public async Task PlayCard(SKCard card)
        {
            if (Table.Table.State == (int)TableState.Playing && Table.Table.CurrentPlayer == Table.ClientPosition) {
                Table.Trick.TrickCard = new CardInfo()
                {
                    pos = Table.ClientPosition,
                    rank = card.Rank,
                    suit = card.Suit
                };
                await hub.PlayCard(card.Rank, card.Suit);
            }
        }

        public async Task NextRound()
        {
            await hub.NextRound();
        }

        public void ShowHands()
        {
            showHands = !showHands;
        }

        public void DebugPlayCard()
        {
            Random random = new Random();
            CardInfo card = new CardInfo()
            {
                pos = Table.Table.CurrentPlayer,
                rank = random.Next(0, 8),
                suit = random.Next(0, 4)
            };
            Table.Trick.TrickCard = card;
            Table.Table.PlayCard(card.rank, card.suit);
        }

        public void Update()
        {
            InvokeAsync(() => StateHasChanged());
        }
    }
}
