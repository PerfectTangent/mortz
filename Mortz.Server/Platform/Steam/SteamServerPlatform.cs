using System.Net;
using JetBrains.Annotations;
using Mortz.Net.Gns;
using Mortz.Protocol.Net.Query;
using Mortz.Server.Admission;
using Mortz.Server.Players;
using Serilog;

namespace Mortz.Server.Platform.Steam;

/// <summary>Steam login, ticket verification and server-browser publication.</summary>
[UsedImplicitly] // used by the steam flavor
public sealed class SteamServerPlatform : IServerPlatform, IVerificationBackend
{
    private readonly Func<IEnumerable<Player>> _players;
    private readonly Func<ulong> _clock;
    private readonly ILogger _log;
    private SteamGameServerApi? _api;
    private ServerPublication? _publication;
    private bool _disposed;
    private bool _loginSettled;

    public VerificationSessions Verification { get; }

    /// <summary>Steam's game-server sockets, for relay connections.</summary>
    public GnsLibrary? Networking { get; private set; }

    public SteamServerPlatform(Func<IEnumerable<Player>> players, Func<ulong> clock, ILogger log)
    {
        _players = players;
        _clock = clock;
        _log = log;
        Verification = new VerificationSessions(this);
    }

    bool IVerificationBackend.Available => AuthenticationAvailable;
    ulong IVerificationBackend.ServerAccountId => AuthenticationAvailable ? _api!.AccountId : 0;
    bool IVerificationBackend.Begin(byte[] ticket, ulong accountId) => _api!.BeginAuthSession(ticket, accountId) == 0;
    void IVerificationBackend.End(ulong accountId) => _api?.EndAuthSession(accountId);

    public bool AuthenticationAvailable => _api is { } api && api.LoggedOn() && api.AccountId != 0;
    public bool PublicationActive => AuthenticationAvailable && _publication!.Active;
    public string Status { get; private set; } = "Steam unavailable, guest play still works.";
    public bool LoginPending => _api != null && !_loginSettled;

    public bool Initialize(ServerInfo info, bool isPublic)
    {
        if (_api != null || _disposed)
            throw new InvalidOperationException("Steam server platform already started or disposed.");
        try
        {
            _api = SteamGameServerApi.Initialize(info.AppId, checked((ushort)info.GamePort), info.Version);
        }
        catch (Exception exception) when (exception is InvalidOperationException or DllNotFoundException
                                              or EntryPointNotFoundException or BadImageFormatException)
        {
            Status = "Steam failed to start, guest play still works.";
            _log.Warning("Steam server initialization failed: {Reason}", exception.Message);
            return false;
        }
        _api.Connected += Connected;
        _api.ConnectFailure += ConnectFailure;
        _api.Disconnected += Disconnected;
        _api.AuthTicketValidated += Validated;
        _api.FakeIpResult += FakeIpGranted;
        _publication = new ServerPublication(_api, isPublic);
        _api.Advertise(false);
        _api.SetProduct(info.AppId.ToString());
        _api.SetGameDescription("Mortz");
        _api.SetModDir("mortz");
        _api.SetDedicatedServer(true);
        _publication.Advance(info, [], false, _clock());
        StartNetworking(isPublic);
        _api.LogOnAnonymous();
        Status = "Logging on to Steam, guest play already works.";
        return true;
    }

    private void StartNetworking(bool isPublic)
    {
        try
        {
            Networking = GnsLibrary.Steam(SteamGameServerApi.Library, GnsFlavor.STEAM_GAME_SERVER);
            Networking.InitAuthentication();
            Networking.InitRelayNetworkAccess();
            // Steam lists us under the FakeIP, so the browser finds us without open ports.
            if (isPublic && !Networking.RequestFakeIp())
                _log.Warning("could not ask Steam for a FakeIP, the server browser won't list us");
        }
        catch (Exception exception) when (exception is GnsException or EntryPointNotFoundException)
        {
            _log.Warning("Steam relay networking unavailable: {Reason}", exception.Message);
            Networking = null;
        }
    }

    public void Advance(ServerInfo info)
    {
        if (_api == null)
            return;
        Verification.Pump(_api.RunCallbacks);
        _publication!.Advance(info, _players(), AuthenticationAvailable, _clock());
    }

    private void FakeIpGranted(int result)
    {
        if (result == 1 && Networking?.FakeIp is IPEndPoint fakeIp)
            _log.Information("listed on Steam as {FakeIp}", fakeIp);
        else
            _log.Warning("Steam gave us no FakeIP ({Result}), the server browser won't list us", result);
    }

    private void Validated(ulong accountId, int response, ulong ownerId) => Verification.Result(accountId, response == 0);

    private void Connected()
    {
        _loginSettled = true;
        Status = "Steam connected.";
        _log.Information("logged on to Steam as {SteamId}", _api!.AccountId);
    }

    private void ConnectFailure(int result, bool retrying)
    {
        _loginSettled = true;
        Status = $"Steam login failed ({result}), guest play still works.";
    }

    private void Disconnected(int result)
    {
        _loginSettled = true;
        Status = $"Steam disconnected ({result}), guest play still works.";
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _publication?.Dispose();
        Verification.Dispose();
        if (_api != null)
        {
            _api.LogOff();
            _api.Shutdown();
            _api = null;
        }
    }
}
