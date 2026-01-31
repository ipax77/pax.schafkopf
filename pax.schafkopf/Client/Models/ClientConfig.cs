using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace pax.schafkopf.Client.Models
{
    public class ClientConfig
    {
        [Required]
        [Range(1, int.MaxValue, ErrorMessage="Muss größer 0 sein.")]
        public int RufValue { get; set; } = 10;
        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "Muss größer 0 sein.")]
        public int SoloValue { get; set; } = 20;
        [Required]
        [Range(1, int.MaxValue, ErrorMessage = "Muss größer 0 sein.")]
        public int AddValue { get; set; } = 10;
        [Required]
        [Range(12, int.MaxValue, ErrorMessage = "Muss größer 11 sein.")]
        public int MaxRounds { get; set; } = 32;
        public bool Ramsch { get; set; } = false;
        public bool Hochzeit { get; set; } = false;
        public bool Geier { get; set; } = false;
    }
}
