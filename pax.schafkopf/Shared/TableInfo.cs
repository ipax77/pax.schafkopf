using pax.schafkopf.lib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace pax.schafkopf.Shared
{
    public class TableInfo
    {
        public List<PlayerInfo> playerInfos { get; set; }
        public SKTable Table { get; set; }
        public int myPosition { get; set; }
        public int Visitors { get; set; }
    }
}
