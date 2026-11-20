namespace sk.shared;

public partial class PublicGameState
{
    public List<GameAction> GetLegalActions()
    {
        if (YourPosition is not (>= 0 and < 4) || ActivePlayer != YourPosition) return [];
        var hand = Table.Players[ActivePlayer].Hand;
        return GameState switch
        {
            GameState.Bidding1 => [GameAction.Interest(false), GameAction.Interest(true)],
            GameState.Bidding2 when Bidding1Result != null => GameRules.LegalBids(hand,
                !IsLastInterestedPlayer() || Bidding2Result is { GameType: not GameType.None }),
            GameState.Playing when Bidding2Result != null => GameRules.LegalCards(hand, Bidding2Result,
                Table.CurrentTrick[LeadingPlayer], DrunterDurch).Select(GameAction.Play).ToList(),
            _ => []
        };
    }

    public List<GameType> GetValidGameModes() => GetLegalActions()
        .Where(a => a.Kind is ActionKind.Bid or ActionKind.Pass)
        .Select(a => a.GameType).Distinct().ToList();

    public List<Suit> GetValidSuits(GameType gameType) => GetLegalActions()
        .Where(a => a.Kind == ActionKind.Bid && a.GameType == gameType)
        .Select(a => a.Suit).Where(s => s != Suit.None).Distinct().ToList();

    public List<Card> GetValidCards() => GetLegalActions().Where(a => a.Kind == ActionKind.Play)
        .Select(a => GameAction.FromCardId(a.CardId)).ToList();

    private bool IsLastInterestedPlayer() => Bidding1Result!.InterestedPlayers
        .MaxBy(p => (p - LeadingPlayer + 4) % 4) == YourPosition;
}
