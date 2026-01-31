using pax.schafkopf.lib;
using pax.schafkopf.lib.Enums;
using pax.schafkopf.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace pax.schafkopf.Client.Services
{
    public static class CardService
    {
        // ratio 0.55 / 1.8181556
        public const float AbsHeight = 378.5f;
        public const float AbsWidth = 208.4f;
        public static float VPHeight { get; set; } = 40;
        public static float VPWidth => VPHeight * 0.55f;

        public static string GetImageString(SKCard card)
        {
            // https://github.com/thielepaul/Schafkopf/tree/master/Schafkopf/wwwroot/carddecks
            // return $"images/{card.rank}{card.suit.ToString("g")}.jpg";
            if (card == null)
                return "";
            return $"images/noto/{(Rank)card.Rank}{(Suit)card.Suit}.svg";
        }

        public static string GetImageString(CardInfo card)
        {
            // https://github.com/thielepaul/Schafkopf/tree/master/Schafkopf/wwwroot/carddecks
            // return $"images/{card.rank}{card.suit.ToString("g")}.jpg";
            if (card == null)
                return "";
            return $"images/noto/{(Rank)card.rank}{(Suit)card.suit}.svg";
        }
    }
}
