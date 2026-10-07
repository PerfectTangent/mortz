namespace Mortz.Server.Hooks;

/// <summary>The phase just opened. Every connected player is its transition cohort.</summary>
public interface IBeginPhase
{
    void Begin();
}
