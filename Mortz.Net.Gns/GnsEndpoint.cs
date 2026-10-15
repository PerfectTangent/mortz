namespace Mortz.Net.Gns;

/// <summary>Called from <see cref="GnsEndpoint.Pump"/>, on the pumping thread.</summary>
public interface IGnsHandler
{
    void OnConnected(uint connection);

    /// <summary><paramref name="wasConnected"/> is false when a connect attempt never got through.</summary>
    void OnDisconnected(uint connection, bool wasConnected, int reason, string debug);

    /// <summary>The span is only valid during the call.</summary>
    void OnMessage(uint connection, ReadOnlySpan<byte> data);
}

public abstract unsafe class GnsEndpoint : IDisposable
{
    protected const int RECEIVE_BATCH = 64;

    protected readonly GnsLibrary Lib;
    private readonly Queue<GnsStatusChange> _changes = new();
    protected bool Disposed;

    protected GnsEndpoint(GnsLibrary lib) => Lib = lib;

    internal void Enqueue(GnsStatusChange change) => _changes.Enqueue(change);

    /// <summary>Runs GNS callbacks, then reports status changes and received messages.</summary>
    public void Pump(IGnsHandler handler)
    {
        if (Disposed)
            return;
        Lib.Pump();
        while (_changes.TryDequeue(out GnsStatusChange change))
        {
            OnStatusChange(change, handler);
        }

        Receive(handler);
    }

    protected abstract void OnStatusChange(GnsStatusChange change, IGnsHandler handler);

    protected abstract int ReceiveBatch(nint* messages);

    private void Receive(IGnsHandler handler)
    {
        nint* messages = stackalloc nint[RECEIVE_BATCH];
        int count;
        do
        {
            count = ReceiveBatch(messages);
            for (int i = 0; i < count; i++)
            {
                byte* msg = (byte*)messages[i];
                try
                {
                    var data = new ReadOnlySpan<byte>(*(void**)(msg + GnsMessage.DATA), *(int*)(msg + GnsMessage.SIZE));
                    handler.OnMessage(*(uint*)(msg + GnsMessage.CONNECTION), data);
                }
                finally
                {
                    Lib.ReleaseMessage((nint)msg);
                }
            }
        } while (count == RECEIVE_BATCH && !Disposed);
    }

    public GnsResult Send(uint connection, ReadOnlySpan<byte> data, GnsSend flags)
    {
        fixed (byte* p = data)
            return (GnsResult)Lib.SendMessageToConnection(Lib.Sockets, connection, p, (uint)data.Length, (int)flags, null);
    }

    /// <summary><paramref name="linger"/> keeps the connection alive until queued reliable
    /// messages are delivered. Without it they are dropped.</summary>
    public void Close(uint connection, int reason, string debug, bool linger)
    {
        Lib.Disown(connection);
        Lib.Close(connection, reason, debug, linger);
    }

    public GnsRealTimeStatus? Status(uint connection)
    {
        byte* status = stackalloc byte[GnsRealTimeStatus.SIZE];
        return (GnsResult)Lib.GetConnectionRealTimeStatus(Lib.Sockets, connection, status, 0, null) == GnsResult.OK
            ? GnsRealTimeStatus.Read(status)
            : null;
    }

    public abstract void Dispose();
}
