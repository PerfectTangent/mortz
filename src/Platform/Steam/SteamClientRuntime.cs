#if MORTZ_STEAM
using System.Runtime.InteropServices;
using Godot;
using Mortz.Client.Logging;
using Mortz.Client.Servers;
using Mortz.Client.Session;
using Mortz.Net.Gns;
using SteamApi = GDExtension.Wrappers.Steam;

namespace Mortz.Platform.Steam;

public class SteamClientRuntime : IDisposable
{
    private SteamApi? _api;
    private bool _initialized;
    private bool _disposed;
    private SteamClientTickets? _tickets;
    private SteamInternetDiscovery? _discovery;
    public IInternetDiscovery Discovery => _discovery ?? (IInternetDiscovery)new UnavailableInternetDiscovery();
    public IClientTicketProvider Tickets => _tickets ?? (IClientTicketProvider)new GuestTicketProvider();
    public string? PersonaName => Available ? _api!.GetPersonaName() : null;
    public bool Available => _initialized && _api!.LoggedOn();
    public string Status { get; private set; } = "Steam unavailable, guest play still works.";

    /// <summary>Steam's client networking, for joining servers over Steam Datagram Relay.</summary>
    public GnsLibrary? Relay { get; private set; }

    public void Start()
    {
        if (_api != null || _disposed)
        {
            throw new InvalidOperationException("Steam client runtime already started or disposed.");
        }
        if (!Engine.HasSingleton("Steam"))
        {
            return;
        }
        try
        {
            _api = SteamApi.Bind(Engine.GetSingleton("Steam"));
            // Distributed clients get their App ID from Steam's launch environment.
            _initialized = _api.SteamInit(0, false);
            if (_initialized)
            {
                _discovery = new SteamInternetDiscovery(_api, () => Available);
                _tickets = new SteamClientTickets(_api, () => Available);
                _api.GetAuthSessionTicketResponseSignal += _tickets.Ready;
                Relay = StartRelay();
            }
            Status = _initialized ? "Steam ready." : "Steam unavailable, guest play still works.";
        }
        catch (Exception exception)
        {
            _initialized = false;
            _discovery?.Dispose();
            _discovery = null;
            ReleaseTickets();
            Status = "Steam failed to start, guest play still works.";
            MortzLog.For("steam").Warning("Steam client initialization failed: {Reason}", exception.Message);
        }
        if (!_initialized)
        {
            _api?.SteamShutdown();
        }
        MortzLog.For("steam").Information("{Status}", Status);
    }

    public void Advance()
    {
        if (_initialized && !_disposed)
        {
            _api!.RunCallbacks();
            _discovery?.Advance();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _discovery?.Dispose();
        _discovery = null;
        ReleaseTickets();
        if (_initialized)
        {
            _initialized = false;
            _api!.SteamShutdown();
        }
        // The addon owns its singleton and attached wrapper.
        _api = null;
    }

    private static GnsLibrary? StartRelay()
    {
        string name = OperatingSystem.IsWindows() ? "steam_api64.dll" : "libsteam_api.so";
        try
        {
            // By name, this returns the copy GodotSteam already loaded.
            GnsLibrary relay = GnsLibrary.Steam(NativeLibrary.Load(name), GnsFlavor.STEAM_CLIENT);
            relay.InitAuthentication();
            relay.InitRelayNetworkAccess();
            return relay;
        }
        catch (Exception exception) when (exception is GnsException or DllNotFoundException
                                              or EntryPointNotFoundException)
        {
            MortzLog.For("steam").Warning("Steam relay networking unavailable: {Reason}", exception.Message);
            return null;
        }
    }

    private void ReleaseTickets()
    {
        SteamClientTickets? tickets = _tickets;
        _tickets = null;
        if (tickets == null)
        {
            return;
        }
        _api!.GetAuthSessionTicketResponseSignal -= tickets.Ready;
        tickets.Dispose();
    }
}
#endif
