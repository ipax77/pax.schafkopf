using sk.shared;

namespace sk.weblib.Presentation;

// Presentation events are detached from the moving live state. In particular, a collection
// always retains its winner even if the next trick has already started on the server.
public sealed record TrickStep(Card? Card, int Seat, int? Winner = null, int Points = 0);

public sealed class TrickPlayback
{
    private readonly HashSet<Card> seen = [];
    private int turn;
    private int round = -1;
    private Guid table;
    private long revision = -1;

    public bool NeedsSync(PublicGameState state) => state.Table.Guid != table
        || state.RoundNumber != round || state.Turn > turn + 1 || state.Turn < turn;

    public void Sync(PublicGameState state)
    {
        table = state.Table.Guid;
        round = state.RoundNumber;
        turn = state.Turn;
        revision = state.Revision;
        seen.Clear();
        foreach (var card in state.Table.Players.SelectMany(p => p.Tricks)
            .Concat(state.Table.CurrentTrick.OfType<Card>())) seen.Add(card);
    }

    public IReadOnlyList<TrickStep> Observe(PublicGameState state)
    {
        if (state.Revision < revision) return [];
        var steps = new List<TrickStep>();
        if (state.Turn > turn && state.Table.PreviouseTrick is { Length: 4 } previous)
        {
            int winner = FindWinner(state, previous);
            // Trick ownership also retains the original play order.
            var order = winner < 0 ? previous.OfType<Card>()
                : state.Table.Players[winner].Tricks.Where(c => previous.Contains(c));
            foreach (var card in order)
                if (seen.Add(card)) steps.Add(new(card, Array.IndexOf(previous, card)));
            steps.Add(new(null, -1, winner, previous.OfType<Card>().Sum(c => c.GetValue())));
        }
        for (int offset = 0; offset < 4; offset++)
        {
            int seat = (state.LeadingPlayer + offset) % 4;
            if (state.Table.CurrentTrick[seat] is { } card && seen.Add(card))
                steps.Add(new(card, seat));
        }
        turn = state.Turn;
        revision = state.Revision;
        return steps;
    }

    public static int FindWinner(PublicGameState state, Card?[] cards)
    {
        if (cards.Length != 4 || cards.Any(c => c is null)) return -1;
        return Array.FindIndex(state.Table.Players, player => cards.All(c => player.Tricks.Contains(c!)));
    }

    public static int[] CurrentPlayOrder(PublicGameState state) => Enumerable.Range(0, 4)
        .Select(offset => (state.LeadingPlayer + offset) % 4)
        .Where(seat => state.Table.CurrentTrick[seat] is not null).ToArray();

    public static int[] PreviousPlayOrder(PublicGameState state)
    {
        if (state.Table.PreviouseTrick is not { Length: 4 } cards) return [];
        var winner = FindWinner(state, cards);
        return winner < 0 ? [] : state.Table.Players[winner].Tricks
            .Where(cards.Contains).Select(card => Array.IndexOf(cards, card)).ToArray();
    }
}
