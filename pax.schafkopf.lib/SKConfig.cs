using System;
using System.Collections.Generic;
using System.Text;

namespace pax.schafkopf.lib
{
    public class SKConfig
    {
        public int RufValue { get; set; } = 10;
        public int SoloValue { get; set; } = 20;
        public int AddValue { get; set; } = 10;
        public int MaxRounds { get; set; } = 32;
        public int MaxCardsForRufSauSchmieren = 2;
        public bool playRamsch { get; set; } = false;
    }
}
