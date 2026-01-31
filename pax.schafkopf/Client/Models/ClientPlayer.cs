using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace pax.schafkopf.Client.Models
{
    public class ClientPlayer
    {
        [Required]
        [MaxLength(16, ErrorMessage = "Maximal 16 Zeichen.")]
        public string Name { get; set; }
        [Required]
        [MaxLength(32, ErrorMessage = "Maximal 32 Zeichen.")]
        public string AuthName { get; set; }
        public int Position { get; set; }
        public int TablePosition { get; set; }
        public bool isDisconnected { get; set; } = false;
        public int GameMode { get; set; }
        public int GameSuit { get; set; }
    }
}
