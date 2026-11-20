using sk.shared;

namespace sk.training;

public sealed record EpisodeTrace(string RulesVersion, ulong Seed, int Dealer, ulong SamplingSeed,
    string[] Policies, string[] PolicyVersions, int[] Actions, int[] Rewards,
    ContractObservation? Contract, string[] Roles, int[] AuctionPrefix, string? Failure = null);

public sealed class EpisodeFailureException(string message, EpisodeTrace trace, Exception inner)
    : Exception(message, inner)
{
    public EpisodeTrace Trace { get; } = trace;
}

public static class EpisodeRunner
{
    public static int[]? ControlledAuction(ulong seed, int dealer, int declarer, int bidId)
    {
        var bid = GameAction.FromId(bidId);
        if (bid.Kind != ActionKind.Bid) throw new ArgumentException("Controlled contract must be action 3..16.");
        var game = new HeadlessGame();
        game.Reset(seed, dealer);
        var actions = new List<int>();
        for (int i = 0; i < 4; i++)
        {
            int id = game.CurrentPlayer == declarer ? 1 : 0;
            actions.Add(id);
            game.Step(id);
        }
        if (!game.LegalActions().Contains(bid)) return null;
        actions.Add(bidId);
        return actions.ToArray();
    }

    public static EpisodeTrace Play(ulong seed, int dealer, ulong samplingSeed, string[] policyNames,
        int[]? auctionPrefix = null, Func<int, IGamePolicy>? policyFactory = null)
    {
        if (policyNames.Length != 4) throw new ArgumentException("Exactly four policies are required.");
        var policies = Enumerable.Range(0, 4).Select(seat => policyFactory?.Invoke(seat) ??
            PolicyCatalog.Create(policyNames[seat], StableRandom.Mix(samplingSeed ^ (ulong)seat))).ToArray();
        var versions = policyNames.Select(PolicyCatalog.Version).ToArray();
        var game = new HeadlessGame();
        game.Reset(seed, dealer);
        var actions = new List<int>(40);
        int[] rewards = [0, 0, 0, 0];
        auctionPrefix ??= [];
        try
        {
            while (!game.IsTerminal)
            {
                if (actions.Count >= 40) throw new InvalidOperationException("Episode exceeded 8 auction + 32 play actions.");
                var observation = game.Observe(game.CurrentPlayer);
                var action = actions.Count < auctionPrefix.Length ? GameAction.FromId(auctionPrefix[actions.Count]) :
                    policies[game.CurrentPlayer].ChooseAction(observation);
                if (!observation.LegalActions.Contains(action))
                    throw new InvalidOperationException($"Policy returned illegal action {action} at decision {actions.Count}.");
                var result = game.Step(action);
                actions.Add(action.Id);
                if (result.Terminal) rewards = result.Rewards.ToArray();
            }
            if (rewards.Sum() != 0) throw new InvalidOperationException("Payouts do not sum to zero.");
            var final = game.Observe(0);
            if (final.Contract != null && (final.Points.Sum() != 120 || final.TrickCounts.Sum() != 8))
                throw new InvalidOperationException("Terminal card/point conservation failed.");
            return Trace(final);
        }
        catch (Exception ex) when (ex is not EpisodeFailureException)
        {
            throw new EpisodeFailureException($"Episode failed: seed={seed}, dealer={dealer}, decision={actions.Count}: {ex.Message}",
                Trace(game.Observe(0)) with { Failure = ex.Message }, ex);
        }

        EpisodeTrace Trace(GameObservation final) => new(HeadlessGame.RulesVersion, seed, dealer, samplingSeed,
            (string[])policyNames.Clone(), versions, actions.ToArray(), rewards, final.Contract,
            Enumerable.Range(0, 4).Select(seat => final.Contract == null ? "all-pass" :
                seat == final.Contract.Player ? "declarer" :
                final.Contract.GameType == GameType.Ruf && seat == final.PublicTeammate ? "partner" : "defender").ToArray(),
            (int[])auctionPrefix.Clone());
    }

    public static StepResult Replay(EpisodeTrace trace, bool requireTerminal = true)
    {
        if (trace.RulesVersion != HeadlessGame.RulesVersion) throw new InvalidOperationException("Replay rules version mismatch.");
        var game = new HeadlessGame();
        game.Reset(trace.Seed, trace.Dealer);
        StepResult result = new(false, new int[4]);
        foreach (int action in trace.Actions) result = game.Step(action);
        if (requireTerminal && (!result.Terminal || !result.Rewards.SequenceEqual(trace.Rewards)))
            throw new InvalidOperationException("Replay did not reproduce terminal rewards.");
        return result;
    }
}
