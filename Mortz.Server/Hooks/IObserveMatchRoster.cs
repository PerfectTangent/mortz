namespace Mortz.Server.Hooks;

/// <summary>The match finished applying a roster change.</summary>
public interface IObserveMatchRoster
{
    void RosterChanged();
}
