
using System.ComponentModel.DataAnnotations;

namespace sk.shared;

public sealed record ConnectInfo
{
    [Required]
    public string UserName { get; set; } = string.Empty;
    public Guid Guid { get; set; } = Guid.NewGuid();
    public Guid TableGuid { get; set; }
    public DateTime LastConnected { get; set; }
    public DateTime LastPlayed { get; set; }
}