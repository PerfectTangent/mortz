
namespace Mortz.Core.Match.Objectives;

public abstract record ObjectiveState;

public record NoObjectiveState : ObjectiveState
{
    public static NoObjectiveState Instance { get; } = new();
}
