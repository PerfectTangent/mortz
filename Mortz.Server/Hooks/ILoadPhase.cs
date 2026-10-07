using Mortz.Server.Phases;
using Mortz.Server.Players;

namespace Mortz.Server.Hooks;

/// <summary>Initial is true for the transition cohort.</summary>
public readonly record struct PhaseLoad(ServerPhaseKind Kind, int Generation, bool Initial);

/// <summary>A player must load the current phase.</summary>
public interface ILoadPhase
{
    void Load(Player player, PhaseLoad load);
}
