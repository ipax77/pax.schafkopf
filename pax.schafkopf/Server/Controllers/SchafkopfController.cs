using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using pax.schafkopf.Server.Models;
using pax.schafkopf.Server.Services;
using pax.schafkopf.Shared;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace pax.schafkopf.Server.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class SchafkopfController : ControllerBase
    {
        private readonly ILogger<SchafkopfController> _logger;

        public SchafkopfController(ILogger<SchafkopfController> logger)
        {
            _logger = logger;
        }

        [HttpPost]
        public IActionResult Post(TableRequest request)
        {
            _logger.LogInformation($"request reconnect {request.Guid}");
            if (!TableService.Tables.ContainsKey(request.Guid))
                return null;
            ServerTable table = TableService.Tables[request.Guid];
            var player = table.Players.FirstOrDefault(f => f.Name == request.Name && f.AuthName == request.AuthName);
            if (player == null)
                return null;
            _logger.LogInformation($"request reconnect info for {player.Position}");
            var info = table.GetTableInfo(player.Position);
            var json = JsonSerializer.Serialize(info, new JsonSerializerOptions() { WriteIndented = true });
            _logger.LogInformation(json);
            return Ok(info);
        }

        [HttpGet("{guid}/{name}/{authname}/{connectionid}")]
        public IActionResult Get(string guid, string name, string authname, string connectionid)
        {
            _logger.LogInformation($"request reconnect {guid} {name} {authname}");
            Guid tableid;
            if (Guid.TryParse(guid, out tableid))
            {
                if (!TableService.Tables.ContainsKey(tableid))
                    return NoContent();
                ServerTable table = TableService.Tables[tableid];
                var player = table.Players.FirstOrDefault(f => f.Name == name && f.AuthName == authname);
                if (player == null)
                    return NoContent();
                player.isDisconnected = false;
                player.ConnectionID = connectionid;
                var info = table.GetTableInfo(player.Position);
                return Ok(info);
            }
            else
                return NoContent();
        }
    }
}
