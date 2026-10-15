using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

[assembly: DisableRuntimeMarshalling]

namespace Mortz.Net.Gns;

/// <summary>Loaded once per process and never torn down: GNS keeps a service thread and global state.</summary>
/// <remarks>
/// Call everything except <see cref="SetGlobalFakeNetwork"/> from the pump thread. Status
/// callbacks fire inside <c>RunCallbacks</c> on the calling thread.
/// </remarks>
public sealed unsafe class GnsLibrary
{
    public const string LINUX_FILE = "libGameNetworkingSockets.so";
    public const string WINDOWS_FILE = "GameNetworkingSockets.dll";

    private static readonly Lock _loadLock = new();
    private static GnsLibrary? _instance;

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

    // Listen socket or connection handle -> endpoint, for the global status callback. Pump thread only.
    private readonly Dictionary<uint, GnsEndpoint> _owners = new();

    private GnsLibrary(nint lib)
    {
        nint Fn(string name) => NativeLibrary.GetExport(lib, name);

        var init = (delegate* unmanaged<void*, byte*, byte>)Fn("GameNetworkingSockets_Init");
        byte* err = stackalloc byte[1024];
        if (init(null, err) == 0)
            throw new GnsException($"GameNetworkingSockets_Init failed: {Utf8(err)}");

        Sockets = ((delegate* unmanaged<nint>)Fn("SteamAPI_SteamNetworkingSockets_v009"))();
        Utils = ((delegate* unmanaged<nint>)Fn("SteamAPI_SteamNetworkingUtils_v003"))();

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

        var setDebug = (delegate* unmanaged<nint, int, delegate* unmanaged<int, byte*, void>, void>)
            Fn("SteamAPI_ISteamNetworkingUtils_SetDebugOutputFunction");
        setDebug(Utils, (int)GnsLogLevel.WARNING, &OnDebugOutput);
        var setCallback = (delegate* unmanaged<nint, delegate* unmanaged<nint, void>, byte>)
            Fn("SteamAPI_ISteamNetworkingUtils_SetGlobalCallback_SteamNetConnectionStatusChanged");
        if (setCallback(Utils, &OnStatusChanged) == 0)
            throw new GnsException("could not install the connection status callback");
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
            _instance = new GnsLibrary(NativeLibrary.Load(path));
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
        GnsLibrary? lib = _instance;
        if (lib == null)
            return;
        var change = GnsStatusChange.Read((byte*)info);
        // Accepted connections arrive under their listen socket, our own connects under the handle.
        if (lib._owners.TryGetValue(change.Connection, out GnsEndpoint? owner) ||
            (change.ListenSocket != 0 && lib._owners.TryGetValue(change.ListenSocket, out owner)))
        {
            owner.Enqueue(change);
            return;
        }
        // Its endpoint is gone, so free the handle.
        if (change.State is GnsState.CONNECTING or GnsState.CLOSED_BY_PEER or GnsState.PROBLEM_DETECTED_LOCALLY)
            lib.CloseConnection(lib.Sockets, change.Connection, 0, null, 0);
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
