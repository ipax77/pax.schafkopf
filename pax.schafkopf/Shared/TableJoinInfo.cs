using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace pax.schafkopf.Shared
{
    public class TableJoinInfo
    {
        public List<PlayerInfo> playerInfos { get; set; }
        public int leader { get; set; }
        public int myPos { get; set; }
    }
}
