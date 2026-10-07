using Mortz.Server.Phases;

namespace Mortz.Server.Hooks;

/// <summary>The server entered the lobby or a match.</summary>
public interface IObservePhase
{
    void PhaseChanged(ServerPhaseKind phase);
}
