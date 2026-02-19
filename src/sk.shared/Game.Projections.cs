namespace sk.shared;

public partial class Game
{
    public PublicGameState ToPublicGameState(int forPlayer)
    {
        var publicTable = new Table
        {
            Guid = Table.Guid,
            PreviouseTrick = Table.PreviouseTrick,
            CurrentTrick = Table.CurrentTrick,
            CurrentTrickCard = Table.CurrentTrickCard,
        };

        for (int i = 0; i < 4; i++)
        {
            var originalPlayer = Table.Players[i];
            publicTable.Players[i] = new TablePlayer
            {
                Player = originalPlayer.Player,
                Position = originalPlayer.Position,
                Tricks = originalPlayer.Tricks,
                Hand = i == forPlayer ? originalPlayer.Hand : GetPrivateHand(originalPlayer.Hand),
                IsConnected = originalPlayer.IsConnected,
            };
        }

        PublicGameResult? publicGameResult = null;
        if (GameState == GameState.Finished)
        {
            publicGameResult = new()
            {
                StartingHands = Table.Players.OrderBy(o => o.Position).Select(s => s.StartingHand).ToList(),
                PlayerCashes = Table.Players.OrderBy(o => o.Position).Select(s => s.Cash).ToList(),
                GameResult = GameResults.LastOrDefault() ?? new(),
            };
        }

        return new PublicGameState
        {
            ShortCode = ShortCode,
            GameState = GameState,
            LeadingPlayer = leadingPlayer,
            ActivePlayer = ActivePlayer,
            YourPosition = forPlayer,
            Turn = Turn,
            Bidding1Result = GetPublicBidding1Result(),
            Bidding2Result = GetPublicBidding2Result(),
            PublicTeammate = PublicTeammate,
            DrunterDurch = DrunterDurch,
            Table = publicTable,
            PublicGameResult = publicGameResult,
        };
    }

    private static List<Card> GetPrivateHand(List<Card> hand)
    {
        List<Card> cards = [];
        foreach (var _ in hand)
        {
            cards.Add(new() { Rank = Rank.Seven, Suit = Suit.None });
        }
        return cards;
    }
}
