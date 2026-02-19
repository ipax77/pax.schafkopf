namespace sk.shared;

public partial class Game
{
    public bool TryAssignEmptySeat(Player player)
    {
        var emptySlot = Table.Players.OrderBy(o => o.Position).FirstOrDefault(f => f.Player.Guid == Guid.Empty);
        if (emptySlot is null)
        {
            return false;
        }
        emptySlot.Player = player;
        emptySlot.IsConnected = true;
        return true;
    }

    public bool HasPlayer(Player player)
    {
        var slot = Table.Players.FirstOrDefault(f => f.Player.Guid == player.Guid);
        return slot is not null;
    }

    public TablePlayer? GetTablePlayer(Guid playerId)
    {
        return Table.Players.FirstOrDefault(f => f.Player.Guid == playerId);
    }
}
