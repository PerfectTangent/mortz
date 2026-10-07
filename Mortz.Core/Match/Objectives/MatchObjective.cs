using Mortz.Core.Match.Scoring;
using Mortz.Core.Sim;

namespace Mortz.Core.Match.Objectives;

public abstract class MatchObjective
{
    public abstract ObjectiveState State { get; }

    /// <summary>Set once the objective completes; stays set.</summary>
    public abstract Victor? Winner { get; }

    /// <summary>Advances one playing tick.</summary>
    public abstract void Advance(SimWorld world);
}

public sealed class NoObjective : MatchObjective
{
    public static NoObjective Instance { get; } = new();

    private NoObjective()
    {
    }

    public override ObjectiveState State => NoObjectiveState.Instance;
    public override Victor? Winner => null;
    public override void Advance(SimWorld world) { }
}
