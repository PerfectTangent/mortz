using Mortz.Core.Match.Objectives;
using Mortz.Core.Sim.Modifiers;

namespace Mortz.Core.Match.Configuration;

[ConfigVariant("none", "None", typeof(NoObjectiveRules))]
[ConfigVariant("deadlock", "Deadlock", typeof(DeadlockObjectiveRules))]
public abstract class ObjectiveRules
{
    public abstract ObjectiveRulesSnapshot ToSnapshot();

    public virtual IEnumerable<string> Errors(ModeRules rules) => [];

    public virtual string? MapError(MapZones zones) => null;
}

public partial class NoObjectiveRules : ObjectiveRules;

public sealed partial record NoObjectiveRulesSnapshot
{
    public override MatchObjective Create(MapZones zones) => NoObjective.Instance;
}

public abstract record ObjectiveRulesSnapshot
{
    public abstract ObjectiveRules ToMutable();

    public abstract MatchObjective Create(MapZones zones);
}
