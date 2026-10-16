#if MORTZ_E2E
using Mortz.Core.Match.Scoring;
using Mortz.Core.Sim;
using Mortz.E2E.Protocol;
using Mortz.Server.Hooks;
using Mortz.Server.Match;
using Mortz.Server.Match.Scoring;
using Mortz.Server.Phases;
using Mortz.Server.Players;

namespace Mortz.Server.Diagnostics;

/// <summary>Read-only tap on the server's lifecycle for the E2E driver.</summary>
public sealed class ServerE2EEvents : IObservePlayers, IObservePhase, IObserveMatchUpdate
{
    private readonly Dictionary<int, string> _names = [];

    public IE2EResponder? Responder { get; set; }

    public ServerPhaseKind Phase { get; private set; } = ServerPhaseKind.LOBBY;

    public IReadOnlyDictionary<int, string> Names => _names;

    public void PlayerJoined(Player player)
    {
        _names[player.PeerId] = player.Name;
        Responder?.Emit(new PlayerJoinedEvent(player.PeerId, player.Name, Map(Phase)));
    }

    public void PlayerLeft(Player player)
    {
        _names.Remove(player.PeerId);
        Responder?.Emit(new PlayerLeftEvent(player.PeerId, Map(Phase)));
    }

    public void PhaseChanged(ServerPhaseKind phase)
    {
        Phase = phase;
        Responder?.Emit(new PhaseChangedEvent(Map(phase)));
    }

    public void MatchUpdated(in MatchUpdate update)
    {
        if (Responder is not IE2EResponder responder)
            return;
        if (update.Tick % SimConfig.TICK_RATE == 0)
            responder.Emit(new MatchTickEvent(update.Tick));
        foreach (ScoredKill kill in update.Eliminations)
        {
            responder.Emit(new EliminationEvent(update.Tick,
                kill.Score.KillerId, kill.Score.VictimId,
                kill.Score.Kind == DeathKind.SUICIDE));
        }
        if (update.MatchEnded is Victor victor)
            responder.Emit(Ended(update.Tick, victor));
    }

    public static E2EPhase Map(ServerPhaseKind kind) =>
        kind == ServerPhaseKind.MATCH ? E2EPhase.MATCH : E2EPhase.LOBBY;

    private static MatchEndedEvent Ended(int tick, Victor victor) => victor switch
    {
        Victor.Player player => new MatchEndedEvent(tick, E2EVictorKind.PLAYER, player.PeerId),
        Victor.Team team => new MatchEndedEvent(tick, E2EVictorKind.TEAM, (int)team.Value),
        _ => new MatchEndedEvent(tick, E2EVictorKind.NOBODY, 0),
    };
}
#endif
