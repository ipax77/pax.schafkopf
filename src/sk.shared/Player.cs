
namespace sk.shared;

public sealed class Player
{
    public string Name { get; set; } = string.Empty;
    public Guid Guid { get; set; }
    public string? ConnectionId { get; set; }
}
