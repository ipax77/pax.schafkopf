using pax.schafkopf.lib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace pax.schafkopf.Shared
{
    public class HandInfo
    {
        public int pos { get; set; }
        public IEnumerable<SKCard> cards { get; set; }
    }
}
