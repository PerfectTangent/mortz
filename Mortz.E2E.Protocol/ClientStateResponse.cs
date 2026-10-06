using Mortz.Core.Match.Objectives;
using Mortz.Core.Match.Participation;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;

namespace Mortz.E2E.Protocol;

public sealed record ClientStateResponse(
    Guid Id,
    int LastSnapshotTick,
    Vec2 PredictedPosition,
    E2EObservedPlayer[] Players) : E2EResponse(Id)
{
    public DeadlockObjectiveState? Objective { get; init; }
    public int ObjectiveTick { get; init; } = -1;
    public MatchParticipation? Participation { get; init; }
    public Team? Team { get; init; }
}
