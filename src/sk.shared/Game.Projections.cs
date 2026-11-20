namespace sk.shared;

public partial class Game
{
    public PublicGameState ToPublicGameState(int forPlayer)
    {
        var table = new Table
        {
            Guid = Table.Guid,
            PreviouseTrick = Table.PreviouseTrick?.ToArray(),
            CurrentTrick = Table.CurrentTrick.ToArray(),
            CurrentTrickCard = Table.CurrentTrickCard == null ? null : new()
            { Position = Table.CurrentTrickCard.Position, Card = Table.CurrentTrickCard.Card }
        };
        for (int i = 0; i < 4; i++)
        {
            var p = Table.Players[i];
            table.Players[i] = new()
            {
                Player = CopyPlayer(p.Player), Position = p.Position, Tricks = [.. p.Tricks],
                Hand = i == forPlayer ? [.. p.Hand] : p.Hand.Select(_ => new Card { Suit = Suit.None }).ToList(),
                IsConnected = p.IsConnected, Cash = p.Cash, ReadyForNextRound = p.ReadyForNextRound
            };
        }
        var bidding1 = GetPublicBidding1Result();
        var result = GameResults.LastOrDefault();
        return new()
        {
            ShortCode = ShortCode, GameState = GameState, LeadingPlayer = leadingPlayer,
            ActivePlayer = ActivePlayer, YourPosition = forPlayer, Turn = Turn,
            Bidding1Result = bidding1 == null ? null : new()
            { InterestedPlayers = bidding1.InterestedPlayers.ToArray(), DeclinedPlayers = bidding1.DeclinedPlayers.ToArray() },
            Bidding2Result = GetPublicBidding2Result(), PublicTeammate = PublicTeammate, DrunterDurch = DrunterDurch,
            Table = table,
            PublicGameResult = GameState != GameState.Finished ? null : new()
            {
                StartingHands = Table.Players.Select(p => p.StartingHand.ToList()).ToList(),
                PlayerCashes = Table.Players.Select(p => p.Cash).ToList(),
                GameResult = result == null ? new() : result with
                { Player = CopyPlayer(result.Player), Player2 = result.Player2 == null ? null : CopyPlayer(result.Player2) }
            }
        };
    }

    private static Player CopyPlayer(Player p) => new() { Name = p.Name, Guid = p.Guid };
}
