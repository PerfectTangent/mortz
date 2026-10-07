using Mortz.Client.Players;

namespace Mortz.Client.Hooks;

/// <summary>A player's resolved match stats changed (modifiers applied).</summary>
public interface IObservePlayerStats
{
    void MatchStatsChanged(ClientPlayer player);
}
