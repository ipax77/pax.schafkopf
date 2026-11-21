using sk.bot;
using sk.shared;

namespace sk.training;

/// <summary>Teaching-rule opportunities, not a proof of optimal play or a reward signal.</summary>
public sealed class GameplayDiagnostics
{
    public sealed class GuidanceStats
    {
        public int Opportunities { get; set; }
        public int Followed { get; set; }
        public int Overtakes { get; set; }
        public List<object> Examples { get; } = [];
    }
    public Dictionary<string, GuidanceStats> Stats { get; } = [];
    public void Record(GameObservation observation, GameAction action)
    {
        var advice = GameplayPolicy.PlayAdvice(observation);
        if (advice == null) return;
        if (!Stats.TryGetValue(advice.Reason, out var stats)) Stats[advice.Reason] = stats = new();
        stats.Opportunities++;
        if (advice.OvertakingActions?.Contains(action) == true) stats.Overtakes++;
        if (advice.Actions.Contains(action)) stats.Followed++;
        else if (stats.Examples.Count < 4) stats.Examples.Add(new
        {
            observation.Seat, observation.Contract, observation.PublicTeammate, observation.LeadingPlayer,
            observation.Hand, observation.CurrentTrick, observation.History,
            chosen = action.Id, preferred = advice.Actions.Select(a => a.Id).ToArray()
        });
    }
}
