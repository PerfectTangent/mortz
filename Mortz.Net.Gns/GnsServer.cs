using System.Net;
using System.Net.Sockets;

namespace Mortz.Net.Gns;

/// <summary>Applied to every accepted connection.</summary>
public sealed record GnsServerOptions
{
    /// <summary>GNS default is 512 KB, which drops most of a 4 MB terrain sync queued in one loop.</summary>
    public int SendBufferBytes { get; init; } = 8 * 1024 * 1024;

    /// <summary>GNS default is 256 KB/s, which makes a terrain sync take seconds.</summary>
    public int SendRateBytesPerSec { get; init; } = 16 * 1024 * 1024;

    /// <summary>A bigger inbound message closes the connection (abuse limit).</summary>
    public int RecvMaxMessageBytes { get; init; } = 512 * 1024;

    public int RecvBufferBytes { get; init; } = 1024 * 1024;

    public int RecvBufferMessages { get; init; } = 1000;

    internal GnsConfigValue[] ToConfig() =>
    [
        GnsConfigValue.Int(GnsConfig.SEND_BUFFER_SIZE, SendBufferBytes),
        GnsConfigValue.Int(GnsConfig.SEND_RATE_MIN, SendRateBytesPerSec),
        GnsConfigValue.Int(GnsConfig.SEND_RATE_MAX, SendRateBytesPerSec),
        GnsConfigValue.Int(GnsConfig.RECV_MAX_MESSAGE_SIZE, RecvMaxMessageBytes),
        GnsConfigValue.Int(GnsConfig.RECV_BUFFER_SIZE, RecvBufferBytes),
        GnsConfigValue.Int(GnsConfig.RECV_BUFFER_MESSAGES, RecvBufferMessages),
    ];
}

/// <summary>A listen socket plus a poll group: accepts up to <c>maxConnections</c> and receives
/// from all of them in one call.</summary>
public sealed unsafe class GnsServer : GnsEndpoint
{
    /// <summary>How often <c>port 0</c> retries when the probed port is taken before GNS binds it.</summary>
    public const int PORT_PROBE_ATTEMPTS = 8;

    private readonly uint _listen;
    private readonly uint _pollGroup;
    private readonly int _maxConnections;
    private readonly HashSet<uint> _pending = [];
    private readonly HashSet<uint> _connected = [];

    public int BoundPort { get; }

    public IReadOnlyCollection<uint> Connections => _connected;

    private GnsServer(GnsLibrary lib, uint listen, int maxConnections) : base(lib)
    {
        _listen = listen;
        _maxConnections = maxConnections;
        _pollGroup = lib.CreatePollGroup(lib.Sockets);
        lib.Own(listen, this);
        byte* addr = stackalloc byte[GnsAddress.SIZE];
        BoundPort = lib.GetListenSocketAddress(lib.Sockets, listen, addr) != 0 ? GnsAddress.Port(addr) : -1;
    }

    /// <summary>GNS refuses port 0, so port 0 probes for a free UDP port and retries if something
    /// grabs it before GNS binds.</summary>
    public static GnsServer Listen(GnsLibrary lib, IPAddress bind, int port, int maxConnections, GnsServerOptions? options = null)
    {
        GnsConfigValue[] config = (options ?? new GnsServerOptions()).ToConfig();
        int attempts = port == 0 ? PORT_PROBE_ATTEMPTS : 1;
        for (int attempt = 0; attempt < attempts; attempt++)
        {
            int tryPort = port == 0 ? ProbeFreePort(bind) : port;
            uint listen = CreateListenSocket(lib, bind, (ushort)tryPort, config);
            if (listen != 0)
                return new GnsServer(lib, listen, maxConnections);
        }
        throw new GnsException($"could not listen on {bind}:{port}");
    }

    private static uint CreateListenSocket(GnsLibrary lib, IPAddress bind, ushort port, GnsConfigValue[] config)
    {
        byte* addr = stackalloc byte[GnsAddress.SIZE];
        GnsAddress.Write(addr, bind, port);
        fixed (GnsConfigValue* opts = config)
            return lib.CreateListenSocketIp(lib.Sockets, addr, config.Length, opts);
    }

    private static int ProbeFreePort(IPAddress bind)
    {
        // GNS binds dual-stack IPv6 for "any", so probe the same way to see both families.
        bool any = bind.Equals(IPAddress.Any) || bind.Equals(IPAddress.IPv6Any);
        using var probe = new Socket(any ? AddressFamily.InterNetworkV6 : bind.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        if (any)
            probe.DualMode = true;
        probe.Bind(new IPEndPoint(any ? IPAddress.IPv6Any : bind, 0));
        return ((IPEndPoint)probe.LocalEndPoint!).Port;
    }

    protected override void OnStatusChange(GnsStatusChange change, IGnsHandler handler)
    {
        uint conn = change.Connection;
        switch (change.State)
        {
            case GnsState.CONNECTING when change.ListenSocket == _listen && !_pending.Contains(conn):
                if (_pending.Count + _connected.Count >= _maxConnections)
                {
                    Lib.Close(conn, GnsEndReason.APP_SERVER_FULL, "server full", linger: false);
                    return;
                }
                if ((GnsResult)Lib.AcceptConnection(Lib.Sockets, conn) != GnsResult.OK)
                {
                    Lib.Close(conn, GnsEndReason.APP_GENERIC, "accept failed", linger: false);
                    return;
                }
                Lib.Own(conn, this);
                Lib.SetConnectionPollGroup(Lib.Sockets, conn, _pollGroup);
                _pending.Add(conn);
                break;
            case GnsState.CONNECTED when _pending.Remove(conn):
                _connected.Add(conn);
                handler.OnConnected(conn);
                break;
            case GnsState.CLOSED_BY_PEER or GnsState.PROBLEM_DETECTED_LOCALLY:
                bool wasConnected = _connected.Remove(conn);
                bool wasPending = _pending.Remove(conn);
                // GNS keeps the handle until we close it.
                Lib.Disown(conn);
                Lib.Close(conn, 0, "", linger: false);
                if (wasConnected || wasPending)
                    handler.OnDisconnected(conn, wasConnected, change.EndReason, change.EndDebug);
                break;
        }
    }

    protected override int ReceiveBatch(nint* messages) =>
        Lib.ReceiveMessagesOnPollGroup(Lib.Sockets, _pollGroup, messages, RECEIVE_BATCH);

    /// <summary>Drops a connection we decided to end. No disconnect is reported for it.</summary>
    public void Kick(uint connection, int reason, string debug, bool linger)
    {
        _connected.Remove(connection);
        _pending.Remove(connection);
        Close(connection, reason, debug, linger);
    }

    public override void Dispose()
    {
        if (Disposed)
            return;
        Disposed = true;
        foreach (uint conn in _connected.Concat(_pending).ToArray())
        {
            Close(conn, GnsEndReason.APP_SHUTDOWN, "server stopped", linger: true);
        }
        _connected.Clear();
        _pending.Clear();
        Lib.Disown(_listen);
        Lib.CloseListenSocket(Lib.Sockets, _listen);
        Lib.DestroyPollGroup(Lib.Sockets, _pollGroup);
    }
}
