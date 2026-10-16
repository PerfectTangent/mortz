using System.Buffers.Binary;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

[assembly: DisableRuntimeMarshalling]

namespace Mortz.Net.Gns;

/// <summary>Which GNS build a library binds. Use Steam's only for relays.</summary>
public enum GnsFlavor
{
    OPEN_SOURCE,
    STEAM_CLIENT,
    STEAM_GAME_SERVER,
}

/// <summary>Loaded once per process and flavor and never torn down: GNS keeps a service thread
/// and global state.</summary>
/// <remarks>
/// Call everything except <see cref="SetGlobalFakeNetwork"/> from the pump thread. Status
/// callbacks fire inside <c>RunCallbacks</c> on the calling thread.
/// </remarks>
public sealed unsafe class GnsLibrary
{
    public const string LINUX_FILE = "libGameNetworkingSockets.so";
    public const string WINDOWS_FILE = "GameNetworkingSockets.dll";

    /// <summary>k_ESteamNetworkingAvailability_Current.</summary>
    private const int AVAILABILITY_CURRENT = 100;

    private static readonly Lock _loadLock = new();
    private static GnsLibrary? _instance;
    // Steam's client and game-server sockets share one utils interface, so one status callback.
    private static readonly List<GnsLibrary> _steamInstances = [];

    /// <summary>Native warnings and errors. Can fire on GNS's own threads, not just the pump thread.</summary>
    public static Action<GnsLogLevel, string>? Log { get; set; }

    internal readonly nint Sockets;
    internal readonly nint Utils;

    // C++ references (const SteamNetworkingIPAddr &) are pointers in the ABI, and C++ bool is one byte.
    internal readonly delegate* unmanaged<nint, byte*, int, GnsConfigValue*, uint> CreateListenSocketIp;
    internal readonly delegate* unmanaged<nint, byte*, int, GnsConfigValue*, uint> ConnectByIpAddress;
    internal readonly delegate* unmanaged<nint, uint, int> AcceptConnection;
    internal readonly delegate* unmanaged<nint, uint, int, byte*, byte, byte> CloseConnection;
    internal readonly delegate* unmanaged<nint, uint, byte> CloseListenSocket;
    internal readonly delegate* unmanaged<nint, uint, void*, uint, int, long*, int> SendMessageToConnection;
    internal readonly delegate* unmanaged<nint, uint, nint*, int, int> ReceiveMessagesOnConnection;
    internal readonly delegate* unmanaged<nint, uint, nint*, int, int> ReceiveMessagesOnPollGroup;
    internal readonly delegate* unmanaged<nint, uint> CreatePollGroup;
    internal readonly delegate* unmanaged<nint, uint, byte> DestroyPollGroup;
    internal readonly delegate* unmanaged<nint, uint, uint, byte> SetConnectionPollGroup;
    internal readonly delegate* unmanaged<nint, uint, byte*, int, void*, int> GetConnectionRealTimeStatus;
    internal readonly delegate* unmanaged<nint, uint, byte*, byte> GetListenSocketAddress;
    internal readonly delegate* unmanaged<nint, void> RunCallbacks;
    internal readonly delegate* unmanaged<nint, int, int, nint, int, void*, byte> SetConfigValue;
    internal readonly delegate* unmanaged<nint, void> ReleaseMessage;
    internal readonly delegate* unmanaged<nint, int, int, GnsConfigValue*, uint> CreateListenSocketP2P;
    internal readonly delegate* unmanaged<nint, byte*, int, int, GnsConfigValue*, uint> ConnectP2P;
    private readonly delegate* unmanaged<nint, int> _initAuthentication;
    private readonly delegate* unmanaged<nint, byte*, int> _getAuthenticationStatus;
    private readonly delegate* unmanaged<nint, void> _initRelayNetworkAccess;
    private readonly delegate* unmanaged<nint, byte*, int> _getRelayNetworkStatus;
    private readonly delegate* unmanaged<nint, int, byte> _beginAsyncRequestFakeIp;
    private readonly delegate* unmanaged<nint, int, byte*, void> _getFakeIp;

    public GnsFlavor Flavor { get; }

    // Listen socket or connection handle -> endpoint, for the global status callback. Pump thread only.
    private readonly Dictionary<uint, GnsEndpoint> _owners = new();

    private GnsLibrary(nint lib, GnsFlavor flavor)
    {
        nint Fn(string name) => NativeLibrary.GetExport(lib, name);

        Flavor = flavor;
        if (flavor == GnsFlavor.OPEN_SOURCE)
        {
            var init = (delegate* unmanaged<void*, byte*, byte>)Fn("GameNetworkingSockets_Init");
            byte* err = stackalloc byte[1024];
            if (init(null, err) == 0)
                throw new GnsException($"GameNetworkingSockets_Init failed: {Utf8(err)}");
        }

        (string sockets, string utils) = flavor switch
        {
            GnsFlavor.OPEN_SOURCE => ("SteamAPI_SteamNetworkingSockets_v009", "SteamAPI_SteamNetworkingUtils_v003"),
            GnsFlavor.STEAM_CLIENT => ("SteamAPI_SteamNetworkingSockets_SteamAPI_v012", "SteamAPI_SteamNetworkingUtils_SteamAPI_v004"),
            _ => ("SteamAPI_SteamGameServerNetworkingSockets_SteamAPI_v012", "SteamAPI_SteamNetworkingUtils_SteamAPI_v004"),
        };
        Sockets = ((delegate* unmanaged<nint>)Fn(sockets))();
        Utils = ((delegate* unmanaged<nint>)Fn(utils))();
        if (Sockets == 0 || Utils == 0)
            throw new GnsException($"{flavor} networking interfaces are unavailable");

        const string PREFIX = "SteamAPI_ISteamNetworkingSockets_";
        CreateListenSocketIp = (delegate* unmanaged<nint, byte*, int, GnsConfigValue*, uint>)Fn(PREFIX + "CreateListenSocketIP");
        ConnectByIpAddress = (delegate* unmanaged<nint, byte*, int, GnsConfigValue*, uint>)Fn(PREFIX + "ConnectByIPAddress");
        AcceptConnection = (delegate* unmanaged<nint, uint, int>)Fn(PREFIX + "AcceptConnection");
        CloseConnection = (delegate* unmanaged<nint, uint, int, byte*, byte, byte>)Fn(PREFIX + "CloseConnection");
        CloseListenSocket = (delegate* unmanaged<nint, uint, byte>)Fn(PREFIX + "CloseListenSocket");
        SendMessageToConnection = (delegate* unmanaged<nint, uint, void*, uint, int, long*, int>)Fn(PREFIX + "SendMessageToConnection");
        ReceiveMessagesOnConnection = (delegate* unmanaged<nint, uint, nint*, int, int>)Fn(PREFIX + "ReceiveMessagesOnConnection");
        ReceiveMessagesOnPollGroup = (delegate* unmanaged<nint, uint, nint*, int, int>)Fn(PREFIX + "ReceiveMessagesOnPollGroup");
        CreatePollGroup = (delegate* unmanaged<nint, uint>)Fn(PREFIX + "CreatePollGroup");
        DestroyPollGroup = (delegate* unmanaged<nint, uint, byte>)Fn(PREFIX + "DestroyPollGroup");
        SetConnectionPollGroup = (delegate* unmanaged<nint, uint, uint, byte>)Fn(PREFIX + "SetConnectionPollGroup");
        GetConnectionRealTimeStatus = (delegate* unmanaged<nint, uint, byte*, int, void*, int>)Fn(PREFIX + "GetConnectionRealTimeStatus");
        GetListenSocketAddress = (delegate* unmanaged<nint, uint, byte*, byte>)Fn(PREFIX + "GetListenSocketAddress");
        RunCallbacks = (delegate* unmanaged<nint, void>)Fn(PREFIX + "RunCallbacks");
        SetConfigValue = (delegate* unmanaged<nint, int, int, nint, int, void*, byte>)Fn("SteamAPI_ISteamNetworkingUtils_SetConfigValue");
        ReleaseMessage = (delegate* unmanaged<nint, void>)Fn("SteamAPI_SteamNetworkingMessage_t_Release");
        if (flavor != GnsFlavor.OPEN_SOURCE)
        {
            CreateListenSocketP2P = (delegate* unmanaged<nint, int, int, GnsConfigValue*, uint>)Fn(PREFIX + "CreateListenSocketP2P");
            ConnectP2P = (delegate* unmanaged<nint, byte*, int, int, GnsConfigValue*, uint>)Fn(PREFIX + "ConnectP2P");
            _initAuthentication = (delegate* unmanaged<nint, int>)Fn(PREFIX + "InitAuthentication");
            _getAuthenticationStatus = (delegate* unmanaged<nint, byte*, int>)Fn(PREFIX + "GetAuthenticationStatus");
            _initRelayNetworkAccess = (delegate* unmanaged<nint, void>)Fn("SteamAPI_ISteamNetworkingUtils_InitRelayNetworkAccess");
            _getRelayNetworkStatus = (delegate* unmanaged<nint, byte*, int>)Fn("SteamAPI_ISteamNetworkingUtils_GetRelayNetworkStatus");
            _beginAsyncRequestFakeIp = (delegate* unmanaged<nint, int, byte>)Fn(PREFIX + "BeginAsyncRequestFakeIP");
            _getFakeIp = (delegate* unmanaged<nint, int, byte*, void>)Fn(PREFIX + "GetFakeIP");
        }

        var setDebug = (delegate* unmanaged<nint, int, delegate* unmanaged<int, byte*, void>, void>)
            Fn("SteamAPI_ISteamNetworkingUtils_SetDebugOutputFunction");
        setDebug(Utils, (int)GnsLogLevel.WARNING, &OnDebugOutput);
        var setCallback = (delegate* unmanaged<nint, delegate* unmanaged<nint, void>, byte>)
            Fn("SteamAPI_ISteamNetworkingUtils_SetGlobalCallback_SteamNetConnectionStatusChanged");
        if (setCallback(Utils, flavor == GnsFlavor.OPEN_SOURCE ? &OnStatusChanged : &OnSteamStatusChanged) == 0)
            throw new GnsException("could not install the connection status callback");
    }

    /// <summary>Steam's own GNS inside a loaded <c>libsteam_api</c>. Start the game server or
    /// <c>SteamAPI_Init</c> first, and pump Steam's callbacks.</summary>
    public static GnsLibrary Steam(nint steamApi, GnsFlavor flavor)
    {
        if (flavor == GnsFlavor.OPEN_SOURCE)
            throw new ArgumentOutOfRangeException(nameof(flavor));
        lock (_loadLock)
        {
            if (_steamInstances.Find(lib => lib.Flavor == flavor) is { } existing)
                return existing;
            GnsLibrary lib = new(steamApi, flavor);
            _steamInstances.Add(lib);
            return lib;
        }
    }

    /// <summary>Starts pinging relays.</summary>
    public void InitRelayNetworkAccess()
    {
        RequireSteam();
        _initRelayNetworkAccess(Utils);
    }

    public bool RelayNetworkReady
    {
        get
        {
            RequireSteam();
            // SteamRelayNetworkStatus_t: availability first, 272 bytes in all.
            byte* status = stackalloc byte[272];
            return _getRelayNetworkStatus(Utils, status) == AVAILABILITY_CURRENT;
        }
    }

    /// <summary>Fetches the cert relay connections need. Connect only once
    /// <see cref="AuthenticationReady"/>.</summary>
    public void InitAuthentication()
    {
        RequireSteam();
        _initAuthentication(Sockets);
    }

    public bool AuthenticationReady
    {
        get
        {
            RequireSteam();
            byte* status = stackalloc byte[272];
            return _getAuthenticationStatus(Sockets, status) == AVAILABILITY_CURRENT;
        }
    }

    /// <summary>Asks Steam for a FakeIP, which the server browser lists instead of the public IP.
    /// A game server must call this before it logs on.</summary>
    public bool RequestFakeIp()
    {
        RequireSteam();
        return _beginAsyncRequestFakeIp(Sockets, 1) != 0;
    }

    /// <summary>The FakeIP Steam granted, or null.</summary>
    public IPEndPoint? FakeIp
    {
        get
        {
            RequireSteam();
            // SteamNetworkingFakeIPResult_t: EResult, an identity aligned like a callback, then IP and ports.
            byte* result = stackalloc byte[200];
            _getFakeIp(Sockets, 0, result);
            if (*(int*)result != (int)GnsResult.OK)
                return null;
            byte* address = result + (OperatingSystem.IsWindows() ? 8 : 4) + GnsIdentity.SIZE;
            Span<byte> ip = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(ip, *(uint*)address);
            return new IPEndPoint(new IPAddress(ip), *(ushort*)(address + 4));
        }
    }

    /// <summary>Looks in <paramref name="searchDirs"/>, next to the assemblies, next to the
    /// executable, then in the repo's <c>native/gns/&lt;rid&gt;</c> (walking up from the assemblies).</summary>
    public static GnsLibrary Load(params string[] searchDirs)
    {
        lock (_loadLock)
        {
            if (_instance != null)
                return _instance;
            string path = Locate(searchDirs)
                ?? throw new GnsException($"{FileName} not found (searched {string.Join(", ", Candidates(searchDirs))})");
            _instance = new GnsLibrary(NativeLibrary.Load(path), GnsFlavor.OPEN_SOURCE);
            return _instance;
        }
    }

    public static string FileName => OperatingSystem.IsWindows() ? WINDOWS_FILE : LINUX_FILE;

    public static string RuntimeId => OperatingSystem.IsWindows() ? "win-x64" : "linux-x64";

    public static string? Locate(params string[] searchDirs) =>
        Candidates(searchDirs).Select(dir => Path.Combine(dir, FileName)).FirstOrDefault(File.Exists);

    private static IEnumerable<string> Candidates(string[] searchDirs)
    {
        foreach (string dir in searchDirs)
        {
            yield return dir;
        }

        yield return AppContext.BaseDirectory;
        if (Path.GetDirectoryName(Environment.ProcessPath) is { } exeDir)
            yield return exeDir;
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string native = Path.Combine(dir.FullName, "native", "gns", RuntimeId);
            if (Directory.Exists(native))
            {
                yield return native;
                yield break;
            }
        }
    }

    private void RequireSteam()
    {
        if (Flavor == GnsFlavor.OPEN_SOURCE)
            throw new GnsException("relays need Steam's networking");
    }

    /// <summary>Simulated lag and loss on every connection in the process. GNS only offers them globally.</summary>
    public void SetGlobalFakeNetwork(int sendLagMs, int recvLagMs, float sendLossPercent, float recvLossPercent)
    {
        SetGlobal(GnsConfig.FAKE_PACKET_LAG_SEND, sendLagMs);
        SetGlobal(GnsConfig.FAKE_PACKET_LAG_RECV, recvLagMs);
        SetGlobal(GnsConfig.FAKE_PACKET_LOSS_SEND, sendLossPercent);
        SetGlobal(GnsConfig.FAKE_PACKET_LOSS_RECV, recvLossPercent);
    }

    private void SetGlobal(int key, int value) =>
        SetConfigValue(Utils, key, GnsConfig.SCOPE_GLOBAL, 0, GnsConfig.TYPE_INT32, &value);

    private void SetGlobal(int key, float value) =>
        SetConfigValue(Utils, key, GnsConfig.SCOPE_GLOBAL, 0, GnsConfig.TYPE_FLOAT, &value);

    internal void Own(uint handle, GnsEndpoint owner) => _owners[handle] = owner;

    internal void Disown(uint handle) => _owners.Remove(handle);

    internal void Pump() => RunCallbacks(Sockets);

    internal void Close(uint conn, int reason, string debug, bool linger)
    {
        byte[] text = Encoding.UTF8.GetBytes(debug + "\0");
        fixed (byte* p = text)
            CloseConnection(Sockets, conn, reason, p, linger ? (byte)1 : (byte)0);
    }

    [UnmanagedCallersOnly]
    private static void OnStatusChanged(nint info)
    {
        if (_instance is { } lib)
            lib.Route(GnsStatusChange.Read((byte*)info));
    }

    [UnmanagedCallersOnly]
    private static void OnSteamStatusChanged(nint info)
    {
        var change = GnsStatusChange.Read((byte*)info);
        GnsLibrary? owner = _steamInstances.Find(lib => lib.Owns(change));
        // An unowned change goes to the game server's sockets, which then free the handle.
        (owner ?? _steamInstances.Find(lib => lib.Flavor == GnsFlavor.STEAM_GAME_SERVER) ?? _steamInstances.FirstOrDefault())
            ?.Route(change);
    }

    private bool Owns(GnsStatusChange change) =>
        _owners.ContainsKey(change.Connection) || (change.ListenSocket != 0 && _owners.ContainsKey(change.ListenSocket));

    private void Route(GnsStatusChange change)
    {
        // Accepted connections arrive under their listen socket, our own connects under the handle.
        if (_owners.TryGetValue(change.Connection, out GnsEndpoint? owner) ||
            (change.ListenSocket != 0 && _owners.TryGetValue(change.ListenSocket, out owner)))
        {
            owner.Enqueue(change);
            return;
        }
        // Its endpoint is gone, so free the handle.
        if (change.State is GnsState.CONNECTING or GnsState.CLOSED_BY_PEER or GnsState.PROBLEM_DETECTED_LOCALLY)
            CloseConnection(Sockets, change.Connection, 0, null, 0);
    }

    [UnmanagedCallersOnly]
    private static void OnDebugOutput(int level, byte* message) =>
        Log?.Invoke((GnsLogLevel)level, Utf8(message));

    internal static string Utf8(byte* text) => Marshal.PtrToStringUTF8((nint)text) ?? "";
}

public sealed class GnsException(string message) : Exception(message);

public enum GnsLogLevel
{
    NONE = 0,
    BUG = 1,
    ERROR = 2,
    IMPORTANT = 3,
    WARNING = 4,
    MSG = 5,
}
