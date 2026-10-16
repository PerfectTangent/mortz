using Mortz.Core.Sim;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Admission;

namespace Mortz.Server.Admission;

public readonly record struct AdmittedPlayer(int PeerId, string Name, int Skin);

public interface IServerAdmissionTransport
{
    void Offer(int peerId, AdmissionOffer offer);
    /// <summary>Sends acceptance before opening the gameplay gate on the same reliable ordered path.</summary>
    void Accept(int peerId, AdmissionAccepted accepted);
    /// <summary>Closes the gameplay gate, sends the reason, and ends the transport connection.</summary>
    void Reject(int peerId, AdmissionRejected rejected);
}

/// <summary>Guest-only admission: a compatible hello with a valid profile is admitted at once.</summary>
public sealed class ServerAdmission(IServerAdmissionTransport transport) : IDisposable
{
    private const int ATTEMPT = 1;

    private sealed class Connection(ulong deadline)
    {
        public ulong Deadline { get; } = deadline;
        public bool HelloSeen { get; set; }
        public bool Admitted { get; set; }
    }

    private readonly Dictionary<int, Connection> _connections = [];
    private bool _disposed;

    public int AdmittedCount => _connections.Values.Count(connection => connection.Admitted);
    public event Action<AdmittedPlayer>? Admitted;
    public event Action<int>? Departed;

    public void Connected(int peerId, ulong now)
    {
        if (_disposed)
        {
            transport.Reject(peerId, new AdmissionRejected(0, AdmissionRejection.SERVER_STOPPED));
            return;
        }
        if (_connections.ContainsKey(peerId))
        {
            Reject(peerId, AdmissionRejection.INVALID_STATE);
            return;
        }
        _connections.Add(peerId, new Connection(AdmissionLimits.Deadline(now, AdmissionLimits.HELLO_MS)));
    }

    public void Hello(int peerId, AdmissionHello hello, ulong now)
    {
        if (!_connections.TryGetValue(peerId, out Connection? connection))
            return;
        if (connection.HelloSeen)
        {
            Reject(peerId, AdmissionRejection.INVALID_STATE);
            return;
        }
        if (now >= connection.Deadline)
        {
            Reject(peerId, AdmissionRejection.HELLO_TIMEOUT);
            return;
        }
        connection.HelloSeen = true;
        if (hello.ProtocolVersion != NetConfig.PROTOCOL_VERSION || hello.SchemaHash != NetRegistry.SCHEMA_HASH)
        {
            Reject(peerId, AdmissionRejection.INCOMPATIBLE);
            return;
        }
        if (hello.Name == null || hello.Name.Length > NetConfig.MAX_NAME_LENGTH || hello.Skin is < 0 or >= SimConfig.SKIN_COUNT)
        {
            Reject(peerId, AdmissionRejection.INVALID_PROFILE);
            return;
        }
        if (AdmittedCount >= AdmissionLimits.RESERVATIONS)
        {
            Reject(peerId, AdmissionRejection.SERVER_FULL);
            return;
        }
        transport.Offer(peerId, new AdmissionOffer(ATTEMPT, AdmissionMode.GUEST, 0));
        connection.Admitted = true;
        transport.Accept(peerId, new AdmissionAccepted(ATTEMPT, AdmissionMode.GUEST));
        Admitted?.Invoke(new AdmittedPlayer(peerId, hello.Name, hello.Skin));
    }

    public void Proof(int peerId)
    {
        if (_connections.ContainsKey(peerId))
            Reject(peerId, AdmissionRejection.INVALID_STATE);
    }

    public void Advance(ulong now)
    {
        foreach ((int peerId, Connection connection) in _connections.ToArray())
        {
            if (!connection.Admitted && now >= connection.Deadline)
                Reject(peerId, AdmissionRejection.HELLO_TIMEOUT);
        }
    }

    public void Disconnected(int peerId)
    {
        if (_connections.Remove(peerId, out Connection? connection) && connection.Admitted)
            Departed?.Invoke(peerId);
    }

    private void Reject(int peerId, AdmissionRejection reason)
    {
        if (!_connections.Remove(peerId, out Connection? connection))
            return;
        transport.Reject(peerId, new AdmissionRejected(connection.HelloSeen ? ATTEMPT : 0, reason));
        if (connection.Admitted)
            Departed?.Invoke(peerId);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (int peerId in _connections.Keys.ToArray())
        {
            Reject(peerId, AdmissionRejection.SERVER_STOPPED);
        }
    }
}
