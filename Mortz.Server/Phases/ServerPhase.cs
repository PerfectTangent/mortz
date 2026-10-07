namespace Mortz.Server.Phases;

/// <summary>The feature that anchors a phase scope.</summary>
public abstract class ServerPhase
{
    public abstract ServerPhaseKind Kind { get; }
}
