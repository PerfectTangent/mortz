using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Objectives;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;

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
    public static IMatchObjective Create(ObjectiveRulesSnapshot rules, MapZones zones) => rules switch
    {
        NoObjectiveRulesSnapshot => new NoObjective(),
        DeadlockObjectiveRulesSnapshot deadlock => new DeadlockMatchObjective(deadlock, zones),
        _ => throw new ArgumentOutOfRangeException(nameof(rules)),
    };
}

public class DeadlockMatchObjective : IMatchObjective
{
    private readonly DeadlockObjective _objective;
    private readonly MapZone[] _zones;

    public DeadlockMatchObjective(DeadlockObjectiveRulesSnapshot rules, MapZones zones)
    {
        ModeComposition.ValidateMap(rules, zones);
        _zones = zones.All.Where(zone => zone.HasTag(DeadlockObjectiveRules.CONTROL_TAG)).ToArray();
        _objective = new DeadlockObjective(rules.ToMutable());
    }

    public ObjectiveState State => _objective.State;

    public void Advance(SimWorld world)
    {
        bool blue = false;
        bool red = false;
        foreach (PlayerState player in world.Players.Values)
        {
            if (!player.IsAlive || player.Team == null)
            {
                continue;
            }
            foreach (MapZone zone in _zones)
            {
                if (zone.ContainsPlayer(player))
                {
                    blue |= player.Team == Team.BLUE;
                    red |= player.Team == Team.RED;
                    break;
                }
            }
            if (blue && red)
            {
                break;
            }
        }
        _objective.Advance(blue, red);
    }
}
