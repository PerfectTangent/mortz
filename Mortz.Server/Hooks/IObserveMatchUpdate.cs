using Mortz.Server.Match;

namespace Mortz.Server.Hooks;

/// <summary>A match tick completed.</summary>
public interface IObserveMatchUpdate
{
    void MatchUpdated(in MatchUpdate update);
}
