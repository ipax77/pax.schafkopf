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

        var firstCard = Table.CurrentTrick[leadingPlayer]!;
        int winner = leadingPlayer;
        int bestRank = firstCard.GetCardRank(Bidding2Result.GameType, Bidding2Result.Suit, firstCard);
        for (int i = 1; i < 4; i++)
        {
            int player = (leadingPlayer + i) % 4;
            var card = Table.CurrentTrick[player]!;
            int rank = card.GetCardRank(Bidding2Result.GameType, Bidding2Result.Suit, firstCard);
            if (rank > bestRank) { winner = player; bestRank = rank; }
        }
        // Keep play order for snapshots and the public trick history.
        for (int i = 0; i < 4; i++)
            Table.Players[winner].Tricks.Add(Table.CurrentTrick[(leadingPlayer + i) % 4]!);

        Table.PreviouseTrick = Table.CurrentTrick;
        Table.CurrentTrick = [null, null, null, null];

        ActivePlayer = winner;
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
