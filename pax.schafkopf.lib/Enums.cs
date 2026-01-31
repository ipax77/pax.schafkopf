using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace pax.schafkopf.lib.Enums
{
    public enum TableAction
    {
        Create,
        Join,
        Visit,
        ReConnect,
        Doppelt,
        Bidding1,
        Bidding2,
        Contra,
        Re,
        Playing,
        Finished,
        Waiting
    }

    public enum TableState
    {
        Searching,
        Bidding,
        Bidding2,
        Playing,
        Finished,
        End
    }

    public enum GameMode
    {
        Sie,
        Ramsch,
        Weiter,
        Ruf,
        Wenz,
        Solo,
        WenzTout,
        SoloTout,
    }

    public enum Suit
    {
        Schellen,
        Herz,
        Gras,
        Eichel,
        Farblos
    }

    public enum Rank
    {
        Seven,
        Eight,
        Nine,
        Unter,
        Ober,
        King,
        Ten,
        Ace
    }

    public enum AnimationMode
    {
        Off,
        Fast,
        Slow
    }
}
