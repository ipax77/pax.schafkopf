using Microsoft.AspNetCore.Components;
using pax.schafkopf.Client.Models;
using pax.schafkopf.lib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel;
using pax.schafkopf.Shared;
using pax.schafkopf.Client.Services;
using Microsoft.JSInterop;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace pax.schafkopf.Client.Shared
{
    public partial class TrickComponent : ComponentBase, IDisposable
    {
        [Inject]
        protected ILogger<TrickComponent> logger { get; set; }
        [Inject]
        protected IJSRuntime _js { get; set; }
        [CascadingParameter]
        public ClientTable table { get; set; }

        SKPlayer BottomPlayer => table.Table.Players[table.Players.Single(s => s.TablePosition == 0).Position];
        SKPlayer LeftPlayer => table.Table.Players[table.Players.Single(s => s.TablePosition == 1).Position];
        SKPlayer TopPlayer => table.Table.Players[table.Players.Single(s => s.TablePosition == 2).Position];
        SKPlayer RightPlayer => table.Table.Players[table.Players.Single(s => s.TablePosition == 3).Position];
        string BottomCard => BottomPlayer.TrickCard == null ? "" : CardService.GetImageString(BottomPlayer.TrickCard);
        string LeftCard => LeftPlayer.TrickCard == null ? "" : CardService.GetImageString(LeftPlayer.TrickCard);
        string TopCard => TopPlayer.TrickCard == null ? "" : CardService.GetImageString(TopPlayer.TrickCard);
        string RightCard => RightPlayer.TrickCard == null ? "" : CardService.GetImageString(RightPlayer.TrickCard);
        TrickCard[] trickCards;
        string cardplayed = String.Empty;
        string playclass = String.Empty;
        private CancellationTokenSource source;
        private SemaphoreSlim slim;
        bool isAnimating = false;

        protected override void OnInitialized()
        {
            trickCards = new TrickCard[]
            {
                new TrickCard() { card = BottomCard, zindex = 10 },
                new TrickCard() { card = LeftCard, zindex = 10 },
                new TrickCard() { card = TopCard, zindex = 10 },
                new TrickCard() { card = RightCard, zindex = 10 },
            };

            //trickCards = new TrickCard[]
            //{
            //    new TrickCard() { card = CardService.GetImageString(new SKCard() { Rank = 1, Suit = 2 }), zindex = 10 },
            //    new TrickCard() { card = CardService.GetImageString(new SKCard() { Rank = 1, Suit = 2 }), zindex = 10 },
            //    new TrickCard() { card = CardService.GetImageString(new SKCard() { Rank = 1, Suit = 2 }), zindex = 10 },
            //    new TrickCard() { card = CardService.GetImageString(new SKCard() { Rank = 1, Suit = 2 }), zindex = 10 },
            //};

            source = new CancellationTokenSource();
            slim = new SemaphoreSlim(1, 1);
            table.Trick.PropertyChanged += Trick_PropertyChanged;
            base.OnInitialized();
        }

        private void Trick_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            ClientTrick trick = (ClientTrick)sender;
            int tablepos = table.Players.Single(s => s.Position == trick.TrickCard.pos).TablePosition;
            AnimateCard(tablepos, trick.TrickCard);
        }

        private async Task AnimateCard(int tablepos, CardInfo card)
        {
            playclass = tablepos switch
            {
                1 => "playleftcard",
                2 => "playtopcard",
                3 => "playrightcard",
                0 => "playbottomcard",
                _ => ""
            };
            cardplayed = CardService.GetImageString(card);
            if (isAnimating)
                source.Cancel();
            else
            {
                isAnimating = true;
                if (!source.IsCancellationRequested)
                    try
                    {
                        await Task.Delay(SKData.AnimationTime(table.AnimationMode) - 25, source.Token);
                    }
                    catch (OperationCanceledException) when (source.Token.IsCancellationRequested)
                    {
                        source.Dispose();
                        source = new CancellationTokenSource();
                    }

                cardplayed = String.Empty;
                isAnimating = false;
            }
            await slim.WaitAsync();
            try
            {
                trickCards[tablepos].card = CardService.GetImageString(card);
                trickCards[tablepos].zindex = 10 + trickCards.Count(c => !String.IsNullOrEmpty(c.card));
                StateHasChanged();

                if (trickCards.Count(c => !String.IsNullOrEmpty(c.card)) == 4)
                    await AnimateTrick();
            } catch (Exception e)
            {
                logger.LogError(e.Message);
            }
            finally
            {
                slim.Release();
            }
            
        }

        private async Task AnimateTrick()
        {
            await _js.InvokeVoidAsync("TrickAnimation", "trick");
            await Task.Delay(SKData.AnimationTime(table.AnimationMode) - 25 + 1750);

            Array.ForEach(trickCards, t => t.card = String.Empty);
            StateHasChanged();
        }

        public void Dispose()
        {
            table.Trick.PropertyChanged -= Trick_PropertyChanged;
        }
    }

    class TrickCard
    {
        public string card { get; set; }
        public int zindex { get; set; }
    }
}
