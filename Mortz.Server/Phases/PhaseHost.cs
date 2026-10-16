using Mortz.Core.Features;
using Mortz.Protocol.Net;
using Mortz.Server.Hooks;
using Mortz.Server.Players;
using Serilog;

namespace Mortz.Server.Phases;

/// <summary>How features ask for a phase change. Applied at the end of the server tick.</summary>
public interface IPhaseTransitionRequests
{
    bool RequestStartMatch();

    bool RequestReturnToLobby();
}

/// <summary>Owns the current phase scope and the phase-loading transitions.</summary>
public sealed class PhaseHost(
    FeatureScope root,
    ReadyLink link,
    Roster roster,
    ServerClock clock,
    NetRouter<Player> router,
    ILogger log,
    Func<FeatureScope, LobbyPhase> openLobby,
    Func<FeatureScope, IReadOnlyList<SeatAssignment>, int, MatchPhase> openMatch)
    : IPhaseTransitionRequests
{
    private enum PhaseLoadPurpose
    {
        TRANSITION_COHORT,
        JOIN_IN_PROGRESS,
    }

    private enum Request
    {
        NONE,
        START_MATCH,
        RETURN_TO_LOBBY,
    }

    private readonly Dictionary<int, PhaseLoadPurpose> _loading = [];

    private ServerPhase? _phase;
    private Request _pending;

    public ServerPhaseKind Kind => Phase.Kind;

    public int Generation { get; private set; } = 1;

    public bool InputsAllowed => Phase is not MatchPhase { Running: false };

    private ServerPhase Phase => _phase ??
        throw new InvalidOperationException("the initial phase has not been opened");

    private int NextGeneration => checked(Generation + 1);

    /// <summary>The first lobby has nobody in it yet, so there is nothing to begin or load.</summary>
    public void OpenInitial()
    {
        if (_phase != null)
            throw new InvalidOperationException("the initial phase is already open");
        Generation = NextGeneration;
        FeatureScope scope = root.OpenChild("lobby");
        _phase = openLobby(scope);
        scope.Start();
        Route();
    }

    /// <summary>A new player loads the current phase, or waits in the lobby if joins are closed.</summary>
    public void Load(Player player)
    {
        if (Kind == ServerPhaseKind.LOBBY)
        {
            RaiseLoad(player, initial: false);
            return;
        }

        if (Phase is MatchPhase { AcceptsJoins: false })
        {
            link.WaitForLobby(player.PeerId, Generation);
            return;
        }
        _loading[player.PeerId] = PhaseLoadPurpose.JOIN_IN_PROGRESS;
        RaiseLoad(player, initial: false);
    }

    public void Ready(Player player, int generation)
    {
        CloseMatchLoads();
        if (!link.CanReady(player.PeerId, generation))
            return;
        if (generation != Generation || !_loading.Remove(player.PeerId, out PhaseLoadPurpose purpose))
        {
            link.Ready(player.PeerId, generation);
            return;
        }
        if (purpose == PhaseLoadPurpose.TRANSITION_COHORT)
        {
            link.Ready(player.PeerId, generation);
            ReleaseTransitionCohort();
            return;
        }

        // Admit before the queued bootstrap traffic flushes.
        MatchPhase? match = Phase as MatchPhase;
        bool admitted = match?.TryAdmit(player) == true;
        link.Ready(player.PeerId, generation);
        if (!admitted)
        {
            link.WaitForLobby(player.PeerId, Generation);
            return;
        }
        root.Raise<ISyncJip>(feature => feature.Sync(player));
        if (match!.Running)
            link.Send(player.PeerId, new MatchStartMsg(Generation));
    }

    /// <summary>Late joiners still loading when joins close wait for the next lobby.</summary>
    public void CloseMatchLoads()
    {
        if (Phase is not MatchPhase { AcceptsJoins: false })
            return;
        foreach (Player player in roster)
        {
            if (_loading.Remove(player.PeerId))
                link.WaitForLobby(player.PeerId, Generation);
        }
    }

    public void PlayerDisconnected(Player player)
    {
        if (_loading.Remove(player.PeerId, out PhaseLoadPurpose purpose) &&
            purpose == PhaseLoadPurpose.TRANSITION_COHORT)
            ReleaseTransitionCohort();
    }

    public bool RequestStartMatch()
    {
        if (Kind != ServerPhaseKind.LOBBY)
            return false;
        _pending = Request.START_MATCH;
        return true;
    }

    public bool RequestReturnToLobby()
    {
        if (Kind != ServerPhaseKind.MATCH)
            return false;
        _pending = Request.RETURN_TO_LOBBY;
        return true;
    }

    /// <summary>Runs at the end of the server tick.</summary>
    public void ApplyPendingTransition()
    {
        Request request = _pending;
        _pending = Request.NONE;
        switch (request)
        {
            case Request.START_MATCH when Phase is LobbyPhase { CanStart: true } lobby:
                log.Information("all {Players} player(s) ready, starting match", lobby.Seats.Length);
                SeatAssignment[] seats = lobby.Seats;
                Enter(ServerPhaseKind.MATCH, (scope, generation) => openMatch(scope, seats, generation));
                break;
            case Request.RETURN_TO_LOBBY:
                EnterLobby();
                break;
        }
    }

    private void EnterLobby()
    {
        if (Kind != ServerPhaseKind.MATCH)
            return;
        log.Information("back to lobby ({Players} player(s))", roster.Count);
        Enter(ServerPhaseKind.LOBBY, (scope, _) => openLobby(scope));
    }

    /// <summary>Opens the next phase. Everyone connected becomes its transition cohort.</summary>
    private void Enter(ServerPhaseKind kind, Func<FeatureScope, int, ServerPhase> open)
    {
        Player[] players = [.. roster];
        int generation = NextGeneration;
        foreach (Player player in players)
        {
            link.BeginLoading(player.PeerId, generation, clock.Ms);
        }

        FeatureScope scope = root.OpenChild(kind == ServerPhaseKind.MATCH ? "match" : "lobby");
        _phase = open(scope, generation);
        scope.Start();
        Generation = generation;
        _pending = Request.NONE;
        _loading.Clear();
        if (kind == ServerPhaseKind.MATCH)
        {
            foreach (Player player in players)
            {
                _loading.Add(player.PeerId, PhaseLoadPurpose.TRANSITION_COHORT);
            }
        }
        Route();

        root.Raise<IBeginPhase>(feature => feature.Begin());
        foreach (Player player in players)
        {
            RaiseLoad(player, initial: true);
        }
        root.Raise<IObservePhase>(feature => feature.PhaseChanged(kind));
    }

    private void RaiseLoad(Player player, bool initial)
    {
        PhaseLoad load = new(Kind, Generation, initial);
        root.Raise<ILoadPhase>(feature => feature.Load(player, load));
    }

    private void ReleaseTransitionCohort()
    {
        if (Phase is not MatchPhase { Running: false } match ||
            _loading.Values.Contains(PhaseLoadPurpose.TRANSITION_COHORT))
            return;
        if (roster.Count == 0)
        {
            EnterLobby();
            return;
        }

        match.Run();
        link.Broadcast(new MatchStartMsg(Generation));
        log.Information("all transition players loaded. Match starting");
    }

    private void Route()
    {
        router.MatchGeneration = Generation;
        log.Information("{Routes}", router.Describe());
        log.Debug("{Scopes}", root.Describe());
    }
}
