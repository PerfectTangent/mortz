using Mortz.Core.Features;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;
using Mortz.Protocol.Input;
using Mortz.Server.Content;
using Mortz.Server.Diagnostics;
using Mortz.Server.Hooks;
using Mortz.Server.Match;
using Mortz.Server.Match.Services;
using Mortz.Server.Players;
using Mortz.Server.Settings;
using Serilog;

namespace Mortz.Server.Phases;

/// <summary>Everything whose lifetime is exactly one match.</summary>
public sealed class MatchPhase : ServerPhase, IBeginPhase, IObservePlayers, IReceiveInputs, IAdvance
{
    private readonly FeatureScope _scope;
    private readonly MatchRuntime _runtime;
    private readonly MapSnapshot _map;
    private readonly ILogger _log;
    private readonly IMatchControl _control;
    private readonly IPhaseTransitionRequests _transitions;
    private readonly bool _allowJoinInProgress;
    private readonly IReadOnlyList<SeatAssignment> _seats;

    private MatchPhase(
        FeatureScope scope,
        IReadOnlyList<SeatAssignment> seats,
        int generation,
        ServerBoot boot,
        SettingsService settings,
        IMatchControl control,
        ILogger log,
        IPhaseTransitionRequests transitions)
    {
        _scope = scope;
        _map = settings.Map;
        _log = log;
        _control = control;
        _transitions = transitions;
        _allowJoinInProgress = boot.AllowJoinInProgress;
        _seats = seats;

        _runtime = new MatchRuntime(
            _map.BuildMask(),
            settings.Config,
            boot.VictoryLapTicks,
            scope,
            generation,
            _map.SpawnPoints,
            _map.Zones);
    }

    public bool AcceptsJoins => _runtime.Stage == MatchStage.PLAYING;

    /// <summary>False while the transition cohort is still loading.</summary>
    public bool Running { get; private set; }

    public override ServerPhaseKind Kind => ServerPhaseKind.MATCH;

    public static MatchPhase Open(
        FeatureScope scope,
        IReadOnlyList<SeatAssignment> seats,
        int generation,
        ServerBoot boot,
        SettingsService settings,
        Roster roster,
        IServerLink link,
        ServerClock clock,
        IMatchControl control,
        ILogger log,
        IPhaseTransitionRequests transitions)
    {
        MatchPhase phase = scope.Register(
            new MatchPhase(scope, seats, generation, boot, settings, control, log, transitions));
        MatchRuntime runtime = phase._runtime;
        scope.Own(runtime.Dispose);
        TerrainHistory terrainHistory = scope.Register(new TerrainHistory());
        scope.Register(new MatchReplication(
            runtime,
            roster,
            phase._map,
            terrainHistory,
            link,
            log,
            boot.NetStats,
            boot.AllowJoinInProgress));
        scope.Register(new MatchPointService(link, log));
        scope.Register(new EmptyMatchTimeout(runtime, clock, transitions));
        return phase;
    }

    public void Run() => Running = true;

    public void Begin()
    {
        foreach (SeatAssignment seat in _seats)
        {
            Seat(seat.Player, seat.Team);
        }

        _scope.Raise<IObserveMatchRoster>(feature => feature.RosterChanged());
    }

    public bool TryAdmit(Player player)
    {
        if (!AcceptsJoins)
        {
            return false;
        }
        if (_allowJoinInProgress)
        {
            Seat(player, null);
        }
        else
        {
            _runtime.AddJipSpectator(player);
            _log.Information("player {PeerId} joined as spectator", player.PeerId);
        }

        _scope.Raise<IObserveMatchRoster>(feature => feature.RosterChanged());
        return true;
    }

    public void PlayerJoined(Player player)
    {
    }

    public void PlayerLeft(Player player)
    {
        _runtime.Remove(player);
        _log.Information("player {PeerId} left ({InGame} in game)", player.PeerId,
            _runtime.World.Players.Count);
        _scope.Raise<IObserveMatchRoster>(feature => feature.RosterChanged());
    }

    public void Inputs(Player player, byte[] packet)
    {
        if (!InputPacket.TryDecode(packet, out List<(int Seq, PlayerInput Input)> inputs, out int generation)
            || generation != _runtime.Generation)
        {
            return;
        }

        foreach ((int sequence, PlayerInput input) in inputs)
        {
            _runtime.EnqueueInput(player, sequence, input);
        }
    }

    public void Advance(ServerTime time)
    {
        if (!Running)
            return;
        _control.ApplyBefore(_runtime.World);
        MatchUpdate update = _runtime.Advance(time);
        _control.CompleteAfter(_runtime.World, _runtime.Objective);
        _scope.Raise<IObserveMatchUpdate>(feature => feature.MatchUpdated(update));
        if (update.ReturnToLobby)
            _transitions.RequestReturnToLobby();
    }

    private void Seat(Player player, Team? lobbyTeam)
    {
        if (_map.SpawnPoints.Length > 0 &&
            _runtime.World.Players.Count >= _map.SpawnPoints.Length)
        {
            _log.Warning(
                "map '{MapId}' only has {Spawns} spawn point(s) for {Players} players, " +
                "so some will share one",
                _map.MapId, _map.SpawnPoints.Length, _runtime.World.Players.Count + 1);
        }

        Team? team = _runtime.Seat(player, lobbyTeam);
        string onTeam = team is Team assigned ? $" on {Teams.Name(assigned)}" : "";
        _log.Information("player {PeerId} joined ({InGame} in game){OnTeam:l}", player.PeerId,
            _runtime.World.Players.Count, onTeam);
    }
}
