namespace sk.shared;

public partial class Game
{
    public void PlayCard(int playerIndex, Card card)
    {
        if (playerIndex != ActivePlayer)
            throw new InvalidOperationException("Not this player's turn.");

        var rufAce = Bidding2Result?.GameType == GameType.Ruf
            ? new Card() { Rank = Rank.Ace, Suit = Bidding2Result!.Suit }
            : null;

        if (card == rufAce)
        {
            PublicTeammate = playerIndex;
        }

        if (Table.CurrentTrick.All(a => a == null))
        {
            if (rufAce != null && card.Suit == rufAce.Suit && !card.IsTrump(Bidding2Result!.GameType, Bidding2Result!.Suit)
                && Table.Players[playerIndex].Hand.Contains(rufAce))
            {
                DrunterDurch = true;
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

    public void CollectTrick()
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
        var winner = trick.OrderByDescending(o => o.Card, comparer).First();

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
        bool aTrump = a?.IsTrump(gameType, trump) ?? false;
        bool bTrump = b?.IsTrump(gameType, trump) ?? false;

        if (aTrump != bTrump)
            return aTrump ? 1 : -1;

        if (!aTrump && a?.Suit != b?.Suit)
            return a?.Suit == firstCard.Suit ? 1 : -1;

        return (a?.GetCardRank(gameType, trump, firstCard) ?? 0)
            .CompareTo(b?.GetCardRank(gameType, trump, firstCard) ?? 0);
    }
}
