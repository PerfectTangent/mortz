using Mortz.Core.Features;
using Mortz.Server.Players;

namespace Mortz.Runtime.Tests.Server.Match;

/// <summary>Mints seated players inside one match scope.</summary>
public sealed class MatchSeats
{
    private readonly SortedDictionary<int, Player> _seated = [];

    public FeatureScope Scope { get; } = new("match");

    public IReadOnlyDictionary<int, Player> Seated => _seated;

    public Player GetOrJoin(int peerId)
    {
        if (_seated.TryGetValue(peerId, out Player? present))
            return present;
        Player player = new(peerId, $"Player {peerId}");
        _seated[peerId] = player;
        return player;
    }

    public void Leave(Player player) => _seated.Remove(player.PeerId);
}
