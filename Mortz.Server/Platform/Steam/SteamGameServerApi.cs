using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Mortz.Protocol.Net.Admission;
using Mortz.Protocol.Net.Query;

namespace Mortz.Server.Platform.Steam;

/// <summary>Steam's game-server API over the flat exports of <c>libsteam_api</c>. Call from one thread.</summary>
public sealed unsafe class SteamGameServerApi : IServerPublication
{
    public const string LINUX_FILE = "libsteam_api.so";
    public const string WINDOWS_FILE = "steam_api64.dll";

    /// <summary>EServerMode: Steam authentication, no VAC.</summary>
    private const int SERVER_MODE_AUTHENTICATION = 2;

    /// <summary>STEAMGAMESERVER_QUERY_PORT_SHARED: no query socket, Steam answers browser queries
    /// for our FakeIP.</summary>
    private const ushort QUERY_PORT_SHARED = 0xFFFF;

    // Init fails unless steamclient provides these, so list only the ones this binding calls.
    private const string INTERFACE_VERSIONS =
        "SteamUtils010\0SteamGameServer015\0SteamNetworkingUtils004\0SteamNetworkingSockets012\0\0";

    private const int SERVERS_CONNECTED = 101;
    private const int SERVER_CONNECT_FAILURE = 102;
    private const int SERVERS_DISCONNECTED = 103;
    private const int VALIDATE_AUTH_TICKET_RESPONSE = 143;
    private const int FAKE_IP_RESULT = 1223;

    /// <summary>CallbackMsg_t.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CallbackMsg
    {
        public int User;
        public int Id;
        public byte* Param;
        public int Size;
    }

    private static nint _library;

    private readonly nint _server;
    private readonly int _pipe;
    private bool _shutdown;

    private readonly delegate* unmanaged<nint, byte*, void> _setProduct;
    private readonly delegate* unmanaged<nint, byte*, void> _setGameDescription;
    private readonly delegate* unmanaged<nint, byte*, void> _setModDir;
    private readonly delegate* unmanaged<nint, byte, void> _setDedicatedServer;
    private readonly delegate* unmanaged<nint, void> _logOnAnonymous;
    private readonly delegate* unmanaged<nint, void> _logOff;
    private readonly delegate* unmanaged<nint, byte> _loggedOn;
    private readonly delegate* unmanaged<nint, ulong> _getSteamId;
    private readonly delegate* unmanaged<nint, int, void> _setMaxPlayerCount;
    private readonly delegate* unmanaged<nint, byte*, void> _setServerName;
    private readonly delegate* unmanaged<nint, byte*, void> _setMapName;
    private readonly delegate* unmanaged<nint, byte*, void> _setGameTags;
    private readonly delegate* unmanaged<nint, byte, void> _setAdvertiseServerActive;
    private readonly delegate* unmanaged<nint, void*, int, ulong, int> _beginAuthSession;
    private readonly delegate* unmanaged<nint, ulong, void> _endAuthSession;
    private readonly delegate* unmanaged<nint, ulong> _createUnauthenticatedUserConnection;
    private readonly delegate* unmanaged<nint, ulong, byte*, uint, byte> _updateUserData;
    private readonly delegate* unmanaged<int, void> _runFrame;
    private readonly delegate* unmanaged<int, CallbackMsg*, byte> _getNextCallback;
    private readonly delegate* unmanaged<int, void> _freeLastCallback;
    private readonly delegate* unmanaged<void> _shutdownServer;

    public event Action? Connected;
    public event Action<int, bool>? ConnectFailure;
    public event Action<int>? Disconnected;
    /// <summary>Account, EAuthSessionResponse (0 = OK), owner account.</summary>
    public event Action<ulong, int, ulong>? AuthTicketValidated;
    /// <summary>EResult of the FakeIP request, 1 = OK.</summary>
    public event Action<int>? FakeIpResult;

    private SteamGameServerApi(nint server, int pipe)
    {
        _server = server;
        _pipe = pipe;
        const string PREFIX = "SteamAPI_ISteamGameServer_";
        _setProduct = (delegate* unmanaged<nint, byte*, void>)Fn(PREFIX + "SetProduct");
        _setGameDescription = (delegate* unmanaged<nint, byte*, void>)Fn(PREFIX + "SetGameDescription");
        _setModDir = (delegate* unmanaged<nint, byte*, void>)Fn(PREFIX + "SetModDir");
        _setDedicatedServer = (delegate* unmanaged<nint, byte, void>)Fn(PREFIX + "SetDedicatedServer");
        _logOnAnonymous = (delegate* unmanaged<nint, void>)Fn(PREFIX + "LogOnAnonymous");
        _logOff = (delegate* unmanaged<nint, void>)Fn(PREFIX + "LogOff");
        _loggedOn = (delegate* unmanaged<nint, byte>)Fn(PREFIX + "BLoggedOn");
        _getSteamId = (delegate* unmanaged<nint, ulong>)Fn(PREFIX + "GetSteamID");
        _setMaxPlayerCount = (delegate* unmanaged<nint, int, void>)Fn(PREFIX + "SetMaxPlayerCount");
        _setServerName = (delegate* unmanaged<nint, byte*, void>)Fn(PREFIX + "SetServerName");
        _setMapName = (delegate* unmanaged<nint, byte*, void>)Fn(PREFIX + "SetMapName");
        _setGameTags = (delegate* unmanaged<nint, byte*, void>)Fn(PREFIX + "SetGameTags");
        _setAdvertiseServerActive = (delegate* unmanaged<nint, byte, void>)Fn(PREFIX + "SetAdvertiseServerActive");
        _beginAuthSession = (delegate* unmanaged<nint, void*, int, ulong, int>)Fn(PREFIX + "BeginAuthSession");
        _endAuthSession = (delegate* unmanaged<nint, ulong, void>)Fn(PREFIX + "EndAuthSession");
        _createUnauthenticatedUserConnection = (delegate* unmanaged<nint, ulong>)Fn(PREFIX + "CreateUnauthenticatedUserConnection");
        _updateUserData = (delegate* unmanaged<nint, ulong, byte*, uint, byte>)Fn(PREFIX + "BUpdateUserData");
        _runFrame = (delegate* unmanaged<int, void>)Fn("SteamAPI_ManualDispatch_RunFrame");
        _getNextCallback = (delegate* unmanaged<int, CallbackMsg*, byte>)Fn("SteamAPI_ManualDispatch_GetNextCallback");
        _freeLastCallback = (delegate* unmanaged<int, void>)Fn("SteamAPI_ManualDispatch_FreeLastCallback");
        _shutdownServer = (delegate* unmanaged<void>)Fn("SteamGameServer_Shutdown");
    }

    /// <summary>The loaded <c>libsteam_api</c>.</summary>
    public static nint Library => _library;

    public static string FileName => OperatingSystem.IsWindows() ? WINDOWS_FILE : LINUX_FILE;

    /// <summary>Loads the library from the app directory and starts the game server.</summary>
    public static SteamGameServerApi Initialize(uint appId, ushort gamePort, string version)
    {
        LoadLibrary();
        // Init reads the app id from the native environment, which .NET's copy doesn't reach on Unix.
        SetNativeEnvironment("SteamAppId", appId.ToString(CultureInfo.InvariantCulture));
        var init = (delegate* unmanaged<uint, ushort, ushort, int, byte*, byte*, byte*, int>)
            Fn("SteamInternal_GameServer_Init_V2");
        byte* error = stackalloc byte[1024];
        error[0] = 0;
        int result;
        fixed (byte* versionText = Utf8(version))
        fixed (byte* interfaces = Encoding.ASCII.GetBytes(INTERFACE_VERSIONS))
            result = init(0, gamePort, QUERY_PORT_SHARED, SERVER_MODE_AUTHENTICATION, versionText, interfaces, error);
        if (result != 0)
            throw new InvalidOperationException($"Steam game server init failed ({result}): {Marshal.PtrToStringUTF8((nint)error)}");
        // Manual dispatch only works once the game server exists.
        ((delegate* unmanaged<void>)Fn("SteamAPI_ManualDispatch_Init"))();
        nint server = ((delegate* unmanaged<nint>)Fn("SteamAPI_SteamGameServer_v015"))();
        int pipe = ((delegate* unmanaged<int>)Fn("SteamGameServer_GetHSteamPipe"))();
        return new SteamGameServerApi(server, pipe);
    }

    private static void LoadLibrary()
    {
        if (_library != 0)
            return;
        string[] directories = [AppContext.BaseDirectory, Path.GetDirectoryName(Environment.ProcessPath) ?? ""];
        string path = directories.Select(dir => Path.Combine(dir, FileName)).FirstOrDefault(File.Exists)
            ?? throw new DllNotFoundException($"{FileName} not found next to the server");
        _library = NativeLibrary.Load(path);
    }

    private static nint Fn(string name) => NativeLibrary.GetExport(_library, name);

    public void RunCallbacks()
    {
        _runFrame(_pipe);
        CallbackMsg message;
        while (_getNextCallback(_pipe, &message) != 0)
        {
            try
            {
                Dispatch(message);
            }
            finally
            {
                _freeLastCallback(_pipe);
            }
        }
    }

    private void Dispatch(CallbackMsg message)
    {
        byte* data = message.Param;
        switch (message.Id)
        {
            case SERVERS_CONNECTED:
                Connected?.Invoke();
                break;
            case SERVER_CONNECT_FAILURE:
                ConnectFailure?.Invoke(*(int*)data, data[4] != 0);
                break;
            case SERVERS_DISCONNECTED:
                Disconnected?.Invoke(*(int*)data);
                break;
            case VALIDATE_AUTH_TICKET_RESPONSE:
                // The owner sits after 4 bytes of padding where Windows packs callbacks to 8.
                AuthTicketValidated?.Invoke(*(ulong*)data, *(int*)(data + 8),
                    *(ulong*)(data + (OperatingSystem.IsWindows() ? 16 : 12)));
                break;
            case FAKE_IP_RESULT:
                FakeIpResult?.Invoke(*(int*)data);
                break;
        }
    }

    public void LogOnAnonymous() => _logOnAnonymous(_server);
    public void LogOff() => _logOff(_server);
    public bool LoggedOn() => _loggedOn(_server) != 0;
    public ulong AccountId => _getSteamId(_server);

    /// <summary>EBeginAuthSessionResult: 0 = OK, the result arrives as <see cref="AuthTicketValidated"/>.</summary>
    public int BeginAuthSession(byte[] ticket, ulong account)
    {
        if (ticket.Length is 0 or > AdmissionLimits.TICKET_BYTES)
            throw new ArgumentOutOfRangeException(nameof(ticket));
        fixed (byte* data = ticket)
            return _beginAuthSession(_server, data, ticket.Length, account);
    }

    public void EndAuthSession(ulong account) => _endAuthSession(_server, account);

    public ulong CreateGuest() => _createUnauthenticatedUserConnection(_server);

    public bool Update(ulong accountId, string name)
    {
        fixed (byte* text = Utf8(name))
            return _updateUserData(_server, accountId, text, 0) != 0;
    }

    public void EndGuest(ulong accountId) => EndAuthSession(accountId);

    public void SetProduct(string product) => Call(_setProduct, product);
    public void SetGameDescription(string description) => Call(_setGameDescription, description);
    public void SetModDir(string directory) => Call(_setModDir, directory);
    public void SetDedicatedServer(bool dedicated) => _setDedicatedServer(_server, dedicated ? (byte)1 : (byte)0);
    public void Advertise(bool active) => _setAdvertiseServerActive(_server, active ? (byte)1 : (byte)0);

    public void Publish(ServerInfo info)
    {
        Call(_setServerName, info.Name);
        Call(_setMapName, info.Map);
        _setMaxPlayerCount(_server, info.MaxPlayers);
        Call(_setGameTags, ServerQueryMetadata.ToTags(info));
    }

    public void Shutdown()
    {
        if (_shutdown)
            return;
        _shutdown = true;
        _shutdownServer();
    }

    private void Call(delegate* unmanaged<nint, byte*, void> setter, string value)
    {
        fixed (byte* text = Utf8(value))
            setter(_server, text);
    }

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value + "\0");

    private static void SetNativeEnvironment(string name, string value)
    {
        if (OperatingSystem.IsWindows())
        {
            Environment.SetEnvironmentVariable(name, value);
            return;
        }
        fixed (byte* n = Utf8(name))
        fixed (byte* v = Utf8(value))
            SetEnv(n, v, 1);
    }

    [DllImport("libc", EntryPoint = "setenv")]
    private static extern int SetEnv(byte* name, byte* value, int overwrite);
}
