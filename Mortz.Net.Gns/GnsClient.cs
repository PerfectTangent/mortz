using System.Net;
using System.Net.Sockets;

namespace Mortz.Net.Gns;

public sealed unsafe class GnsClient : GnsEndpoint
{
    private bool _connected;
    private bool _closed;

    public uint Connection { get; }

    private GnsClient(GnsLibrary lib, uint connection) : base(lib)
    {
        Connection = connection;
        lib.Own(connection, this);
    }

    /// <summary>Starts connecting. The result arrives through <see cref="GnsEndpoint.Pump"/>.</summary>
    public static GnsClient Connect(GnsLibrary lib, string host, int port)
    {
        byte* addr = stackalloc byte[GnsAddress.SIZE];
        GnsAddress.Write(addr, Resolve(host), (ushort)port);
        uint conn = lib.ConnectByIpAddress(lib.Sockets, addr, 0, null);
        if (conn == 0)
            throw new GnsException($"could not start a connection to {host}:{port}");
        return new GnsClient(lib, conn);
    }

    /// <summary>Connects through Steam's relays. Call once <see cref="GnsLibrary.AuthenticationReady"/>.</summary>
    public static GnsClient ConnectP2P(GnsLibrary lib, ulong steamId, int virtualPort)
    {
        if (lib.Flavor != GnsFlavor.STEAM_CLIENT)
            throw new GnsException("relay connections need Steam's client networking");
        byte* identity = stackalloc byte[GnsIdentity.SIZE];
        GnsIdentity.WriteSteamId(identity, steamId);
        uint conn = lib.ConnectP2P(lib.Sockets, identity, virtualPort, 0, null);
        if (conn == 0)
            throw new GnsException($"could not start a relay connection to {steamId}");
        return new GnsClient(lib, conn);
    }

    public GnsResult Send(ReadOnlySpan<byte> data, GnsSend flags) =>
        _closed ? GnsResult.NO_CONNECTION : Send(Connection, data, flags);

    protected override void OnStatusChange(GnsStatusChange change, IGnsHandler handler)
    {
        if (change.Connection != Connection || _closed)
            return;
        switch (change.State)
        {
            case GnsState.CONNECTED:
                _connected = true;
                handler.OnConnected(Connection);
                break;
            case GnsState.CLOSED_BY_PEER or GnsState.PROBLEM_DETECTED_LOCALLY:
                _closed = true;
                Close(Connection, 0, "", linger: false);
                handler.OnDisconnected(Connection, _connected, change.EndReason, change.EndDebug);
                break;
        }
    }

    private static IPAddress Resolve(string host)
    {
        if (IPAddress.TryParse(host, out IPAddress? literal))
            return literal;
        IPAddress[] all = Dns.GetHostAddresses(host);
        return all.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
            ?? all.FirstOrDefault()
            ?? throw new GnsException($"could not resolve {host}");
    }

    protected override int ReceiveBatch(nint* messages) =>
        _closed ? 0 : Lib.ReceiveMessagesOnConnection(Lib.Sockets, Connection, messages, RECEIVE_BATCH);

    /// <summary>Queued reliable messages still go out before the connection closes.</summary>
    public override void Dispose()
    {
        if (Disposed)
            return;
        Disposed = true;
        if (!_closed)
        {
            _closed = true;
            Close(Connection, GnsEndReason.APP_GENERIC, "client closed", linger: true);
        }
    }
}
