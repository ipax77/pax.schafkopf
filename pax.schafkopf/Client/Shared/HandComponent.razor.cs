using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using pax.schafkopf.Client.Models;
using pax.schafkopf.lib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace pax.schafkopf.Client.Shared
{
    public partial class HandComponent : ComponentBase
    {
        [CascadingParameter]
        public ClientTable table { get; set; }

        [Parameter]
        public EventCallback<SKCard> OnCardPlayed { get; set; }

        string cardplayed = String.Empty;
        object lockobject = new object();
        SKPlayer Player => table.Table.Players[table.ClientPosition];

        KeyValuePair<int, Vector2> CardTouch = new KeyValuePair<int, Vector2>(-1, Vector2.Zero);
        string dragClass = String.Empty;
        int isInvalid = -1;

        float GetAngle(int cindex)
        {
            float step = 40 / Player.Cards.Count;
            float start = -2.5f * Player.Cards.Count;
            float angle = start + (step * cindex);


            return angle;
        }

        void DragEnd(DragEventArgs e)
        {
            Vector2 dragend = new Vector2((float)e.ClientX, (float)e.ClientY);
            var per = (CardTouch.Value.Y - dragend.Y) / CardTouch.Value.Y * 100;
            if (per > 15)
            {
                lock (lockobject)
                {
                    if (CardTouch.Key >= 0)
                    {
                        PlayCard(CardTouch.Key);
                        CardTouch = new KeyValuePair<int, Vector2>(-1, Vector2.Zero);
                    }
                }
            }
        }


        void DragEnter(DragEventArgs e, int i)
        {
            onCardTouch((float)e.ClientX, (float)e.ClientY, i);
        }

        void TouchEnd(TouchEventArgs e)
        {
            Vector2 touchend = new Vector2((float)e.ChangedTouches.First().PageX, (float)e.ChangedTouches.First().PageY);
            var per = (CardTouch.Value.Y - touchend.Y) / CardTouch.Value.Y * 100;

            if (per > 20)
            {
                lock (lockobject)
                {
                    if (CardTouch.Key >= 0)
                    {
                        PlayCard(CardTouch.Key);
                        CardTouch = new KeyValuePair<int, Vector2>(-1, Vector2.Zero);
                    }
                }
            }
        }

        void TouchStart(TouchEventArgs e, int i)
        {
            onCardTouch((float)e.TargetTouches.First().PageX, (float)e.TargetTouches.First().PageY, i);
            dragClass = "dragging";
        }

        void onCardTouch(float x, float y, int i)
        {
            CardTouch = new KeyValuePair<int, Vector2>(i, new Vector2(x, y));
            //js.InvokeVoidAsync(Player.Cards[i].toId(), CardService.GetImageString(Player.Cards[i]));
        }

        async void PlayCard(int cardindex)
        {
            //logger.LogInformation($"play card {cardindex}");
            //dragClass = String.Empty;
            //Player.TrickCard = Player.Cards[cardindex].Copy();
            //return;
            if (table.Table.CurrentPlayer != Player.Position)
                return;

            if (table.hasDisconnects)
                return;

            var validcards = table.Table.GetValidCards(table.ClientPosition);
            var card = Player.Cards[cardindex];
            if (validcards.FirstOrDefault(f => f.Rank == card.Rank && f.Suit == card.Suit) == null)
            {
                isInvalid = cardindex;
                StateHasChanged();
            }
            else
            {
                await OnCardPlayed.InvokeAsync(Player.Cards[cardindex]);
                isInvalid = -1;
            }
        }
    }
}
