using Mortz.Core.Features;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Query;
using Mortz.Server.Admin;
using Mortz.Server.Admission;
using Mortz.Server.Chat;
using Mortz.Server.Content;
using Mortz.Server.Diagnostics;
using Mortz.Server.Hooks;
using Mortz.Server.Match;
using Mortz.Server.Phases;
using Mortz.Server.Pings;
using Mortz.Server.Players;
using Mortz.Server.Settings;
using Mortz.Server.Wins;
using Serilog;

namespace Mortz.Server;

/// <summary>The dedicated server. Owns the root feature scope.</summary>
public sealed class GameServer : IDisposable, IHandle<Player, PhaseReadyMsg>
{
    private readonly ServerBoot _boot;
    private readonly string _applicationVersion;
    private readonly ReadyLink _link;
    private readonly ILogger _log;
    private readonly ServerClock _clock = new();
    private readonly Roster _roster = new();
    private readonly NetRouter<Player> _router = new();
    private readonly HashSet<ushort> _undispatched = [];
    private readonly FeatureScope _scope;
    private readonly PhaseHost _phases;
    private readonly SettingsService _settings;
    private readonly AdminService _admin;
    private readonly ChatService _chat;
    private readonly IMatchControl _control;

    private bool _disposed;

    /// <summary>Extra root features for process drivers that observe the server.</summary>
    public GameServer(ServerBoot boot, IServerTransport transport, IMapSource maps, ILogger log,
        IMatchControl control, string applicationVersion, IEnumerable<object>? diagnostics = null)
    {
        _boot = boot;
        _applicationVersion = applicationVersion;
        _link = new ReadyLink(transport);
        _log = log;
        _control = control;

        _scope = new FeatureScope("server", _router.Add, _router.Remove);
        _phases = new PhaseHost(_scope, _link, _roster, _clock, _router, log, OpenLobby, OpenMatch);
        _scope.Register(this);
        _settings = _scope.Register(new SettingsService(boot, maps, _link, log));
        _admin = _scope.Register(new AdminService(_scope, _link, _clock, _phases, log, boot.AdminPassword));
        _chat = _scope.Register(new ChatService(_scope, _link, _clock, new Random(boot.Seed)));
        _scope.Register(new TypingService(_scope, _link));
        _scope.Register(new WinsService(_scope, _roster, _link, log));
        _scope.Register(new PingService(_link));
        _scope.Register(new EndMatchService(_admin, _chat, _phases));
        foreach (object feature in diagnostics ?? [])
        {
            _scope.Register(feature);
        }
        _scope.Start();
        _phases.OpenInitial();
    }

    public ServerPhaseKind Phase => _phases.Kind;
    public int Generation => _phases.Generation;

    public int PlayerCount => _roster.Count;

    public void Connect(AdmittedPlayer admitted)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _link.BeginLoading(admitted.PeerId, _phases.Generation, _clock.Ms);
        Player player = _roster.Join(admitted.PeerId, admitted.Name, admitted.Skin);
        _scope.Raise<IObservePlayers>(feature => feature.PlayerJoined(player));
        _phases.Load(player);
    }

    public void Disconnect(int peerId)
    {
        if (_disposed)
            return;
        // Leave the roster first. Tables stay readable until Forget below.
        if (_roster.Leave(peerId) is not Player player)
            return;
        _link.Remove(peerId);
        _scope.RaiseReverse<IObservePlayers>(feature => feature.PlayerLeft(player));
        _scope.Forget(player);
        _phases.PlayerDisconnected(player);
    }

    public void Receive(int peerId, ushort msgId, byte[] payload)
    {
        if (_disposed || _roster.Find(peerId) is not Player player)
            return;
        if (_router.Dispatch(msgId, player, payload))
            return;
        // A client can legitimately race a phase change, so this is noise, not an error.
        if (_undispatched.Add(msgId))
            _log.Information("no handler for {MessageName}", NetRegistry.NameOf(msgId));
    }

    public void Handle(Player player, in PhaseReadyMsg message) =>
        _phases.Ready(player, message.Generation);

    public void Inputs(int peerId, byte[] packet)
    {
        if (_disposed || !_phases.InputsAllowed || _roster.Find(peerId) is not Player player)
            return;
        _scope.Raise<IReceiveInputs>(feature => feature.Inputs(player, packet));
    }

    public void Advance(ServerTime time)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _clock.Ms = time.Ms;
        _link.DisconnectExpired(time.Ms);
        _scope.Raise<IAdvance>(feature => feature.Advance(time));
        _phases.CloseMatchLoads();
        _phases.ApplyPendingTransition();
    }

    public ServerInfo Describe() => new(
        _boot.Name,
        _settings.ModeId,
        _settings.Map.DisplayName,
        _roster.Count,
        NetConfig.MAX_PLAYERS,
        Phase == ServerPhaseKind.LOBBY,
        _boot.AllowJoinInProgress,
        _boot.GamePort,
        NetConfig.PROTOCOL_VERSION,
        NetRegistry.SCHEMA_HASH,
        NetConfig.GAME_APP_ID,
        _applicationVersion);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _router.Clear();
        _scope.Dispose();
    }

    private LobbyPhase OpenLobby(FeatureScope scope) =>
        LobbyPhase.Open(scope, _roster, _settings, _admin, _chat, _link, _log, _phases);

    private MatchPhase OpenMatch(FeatureScope scope, IReadOnlyList<SeatAssignment> seats, int generation) =>
        MatchPhase.Open(scope, seats, generation, _boot, _settings, _roster, _link, _clock, _control, _log,
            _phases);
}
