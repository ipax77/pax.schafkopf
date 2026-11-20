namespace sk.shared;

public partial class Game
{
    public void PlayCard(int playerIndex, Card card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (GameState != GameState.Playing || Bidding2Result == null)
            throw new InvalidOperationException("Not in playing phase.");
        if (playerIndex != ActivePlayer)
            throw new InvalidOperationException("Not this player's turn.");
        if (!Table.Players[playerIndex].GetValidCards(this).Contains(card))
            throw new InvalidOperationException("Illegal card.");

        Revision++;
        _history.Add(new(playerIndex, GameAction.Play(card)));

        var rufAce = Bidding2Result?.GameType == GameType.Ruf
            ? new Card() { Rank = Rank.Ace, Suit = Bidding2Result!.Suit }
            : null;

        if (card == rufAce)
        {
            PublicTeammate = playerIndex;
        }

        if (Table.CurrentTrick.All(a => a == null))
        {
            if (rufAce != null && card != rufAce && card.Suit == rufAce.Suit && !card.IsTrump(Bidding2Result!.GameType, Bidding2Result!.Suit)
                && Table.Players[playerIndex].Hand.Contains(rufAce))
            {
                DrunterDurch = true;
                PublicTeammate = playerIndex;
            }

            leadingPlayer = playerIndex;
        }

        Table.Players[playerIndex].Hand.Remove(card);
        Table.CurrentTrick[playerIndex] = card;

        if (Table.CurrentTrick.All(a => a != null))
        {
            CollectTrick();
            Turn++;
            if (Turn == 8)
            {
                CreateGameResult();
            }
        }
        else
        {
            AdvanceTurn();
        }
        Table.CurrentTrickCard = new() { Position = playerIndex, Card = card };
    }

    private void CollectTrick()
    {
        ArgumentNullException.ThrowIfNull(Bidding2Result);

        var trick = new List<(int Player, Card Card)>();

        int player = leadingPlayer;

        for (int i = 0; i < 4; i++)
        {
            var card = Table.CurrentTrick[player];
            ArgumentNullException.ThrowIfNull(card);

            trick.Add((player, card));
            player = (player + 1) % 4;
        }

        var firstCard = trick[0].Card;
        var comparer = new TrickCardComparer(Bidding2Result.GameType, Bidding2Result.Suit, firstCard);
        var winner = trick[0];
        foreach (var candidate in trick.Skip(1))
            if (comparer.Compare(candidate.Card, winner.Card) > 0) winner = candidate;

        // store trick in play order, not sorted order
        foreach (var (_, card) in trick)
            Table.Players[winner.Player].Tricks.Add(card);

        Table.PreviouseTrick = Table.CurrentTrick;
        Table.CurrentTrick = [null, null, null, null];

        ActivePlayer = winner.Player;
    }

    private void AdvanceTurn()
    {
        ActivePlayer = (ActivePlayer + 1) % 4;
    }
}

public sealed class TrickCardComparer(GameType gameType, Suit trump, Card firstCard) : IComparer<Card>
{
    public int Compare(Card? a, Card? b)
    {
        return (a?.GetCardRank(gameType, trump, firstCard) ?? 0)
            .CompareTo(b?.GetCardRank(gameType, trump, firstCard) ?? 0);
    }
}
