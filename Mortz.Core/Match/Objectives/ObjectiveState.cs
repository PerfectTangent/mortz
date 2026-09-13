using Mortz.Core.Match.Teams;

namespace Mortz.Core.Match.Objectives;

public abstract record ObjectiveState
{
    public virtual Team? CapturedBy => null;
}

public record NoObjectiveState : ObjectiveState
{
    public static NoObjectiveState Instance { get; } = new();
}
