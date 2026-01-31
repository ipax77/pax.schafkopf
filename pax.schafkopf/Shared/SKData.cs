using pax.schafkopf.lib.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace pax.schafkopf.Shared
{
    public static class SKData
    {
        public static Version Version = new Version(0, 2, 3);

        public const string BgColorPlayer = "rgba(255, 193, 7, 0.6)";
        public const string BgColorOnMove = "rgba(255, 193, 7, 0.6)";
        public const string BgColorInfo = "rgba(69, 163, 47, 0.5)";
        public const string BgColorDisconnect = "rgba(250, 15, 50, 0.5)";


        public static string GetGameString(int imode, int itrump, int ipartner)
        {
            GameMode mode = (GameMode)imode;
            Suit trump = (Suit)itrump;
            Suit partner = (Suit)ipartner;

            return (mode, trump, partner) switch
            {
                (GameMode.Ruf, _, Suit.Eichel) => "Auf die Alte",
                (GameMode.Ruf, _, Suit.Gras) => "Auf die Blaue",
                (GameMode.Ruf, _, Suit.Schellen) => "Auf die Lumberte",
                (GameMode.Wenz, _, _) => "Wenz",
                (GameMode.Solo, Suit.Herz, _) => "Herz Solo",
                (GameMode.Solo, Suit.Eichel, _) => "Eichel Solo",
                (GameMode.Solo, Suit.Gras, _) => "Gras Solo",
                (GameMode.Solo, Suit.Schellen, _) => "Schellen Solo",
                _ => "Spielt"
            };
        }

        public static string GetPartnerString(int ipartner)
        {
            Suit rufsau = (Suit)ipartner;
            return rufsau switch
            {
                Suit.Eichel => "Alte",
                Suit.Gras => "Blaue",
                Suit.Schellen => "Lumberte",
                _ => ""
            };
        }

        public static int AnimationTime(AnimationMode mode)
        {
            return mode switch
            {
                AnimationMode.Off => 0,
                AnimationMode.Fast => 750,
                AnimationMode.Slow => 1500,
                _ => 0
            };
        }
    }
}
