using pax.schafkopf.Server.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace pax.schafkopf.Server.Services
{
    public static class TableService
    {
        public static ConcurrentDictionary<Guid, ServerTable> Tables = new ConcurrentDictionary<Guid, ServerTable>();

    }
}
