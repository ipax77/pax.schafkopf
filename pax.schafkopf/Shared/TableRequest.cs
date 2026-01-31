using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace pax.schafkopf.Shared
{
    public class TableRequest
    {
        public Guid Guid { get; set; }
        public string Name { get; set; }
        public string AuthName { get; set; }
    }
}
