using Mortz.Server.Hooks;
using Mortz.Server.Match;
using Mortz.Server.Phases;
using Mortz.Server.Players;

namespace Mortz.Runtime.Tests.Server;

/// <summary>A root feature that records the hooks the server raised.</summary>
public sealed class RecordingObserver : IObservePlayers, IObservePhase, IObserveMatchUpdate
{
    private readonly List<string> _trace = [];

    public IReadOnlyList<string> Trace => _trace;

    public MatchUpdate? LastUpdate { get; private set; }

    public void PlayerJoined(Player player) => _trace.Add($"join:{player.PeerId}");

    public void PlayerLeft(Player player) => _trace.Add($"leave:{player.PeerId}");

    public void PhaseChanged(ServerPhaseKind phase) => _trace.Add($"phase:{phase}");

    public void MatchUpdated(in MatchUpdate update) => LastUpdate = update;
}
