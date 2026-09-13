using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Objectives;
using Mortz.Core.Sim;

namespace Mortz.Server.Match.Modes;

public interface IMatchObjective
{
    ObjectiveState State { get; }

    /// <summary>Advances one playing tick. A completed capture must remain unchanged on subsequent calls.</summary>
    void Advance(SimWorld world);
}

public class NoObjective : IMatchObjective
{
    public ObjectiveState State => NoObjectiveState.Instance;
    public void Advance(SimWorld world) { }
}

public static class MatchObjectives
{
    public static IMatchObjective Create(ObjectiveRulesSnapshot rules) => rules switch
    {
        NoObjectiveRulesSnapshot => new NoObjective(),
        _ => throw new ArgumentOutOfRangeException(nameof(rules)),
    };
}
