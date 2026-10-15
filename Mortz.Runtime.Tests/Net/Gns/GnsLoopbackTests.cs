using System.Diagnostics;
using System.Net;
using Mortz.Net.Gns;
using Xunit;

namespace Mortz.Runtime.Tests.Net.Gns;

/// <summary>GNS behaviour the transport relies on, so a library bump that changes it fails here
/// instead of in a match.</summary>
public sealed class GnsLoopbackTests : IDisposable
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

    private readonly List<IDisposable> _endpoints = [];

    private static GnsLibrary Library()
    {
        Assert.SkipWhen(GnsLibrary.Locate() == null,
            $"{GnsLibrary.FileName} is not built; run tools/native/build-gns.sh");
        return GnsLibrary.Load();
    }

    public void Dispose()
    {
        foreach (IDisposable endpoint in _endpoints)
        {
            endpoint.Dispose();
        }
    }

    private (GnsServer server, Recorder serverSide, GnsClient client, Recorder clientSide) Connected(
        GnsServerOptions? options = null)
    {
        GnsLibrary lib = Library();
        GnsServer server = Track(GnsServer.Listen(lib, IPAddress.Loopback, 0, 8, options));
        GnsClient client = Track(GnsClient.Connect(lib, "127.0.0.1", server.BoundPort));
        Recorder serverSide = new(), clientSide = new();
        PumpUntil(() => serverSide.Connected.Count == 1 && clientSide.Connected.Count == 1,
            (server, serverSide), (client, clientSide));
        return (server, serverSide, client, clientSide);
    }

    private T Track<T>(T endpoint) where T : IDisposable
    {
        _endpoints.Add(endpoint);
        return endpoint;
    }

    private static void PumpUntil(Func<bool> done, params (GnsEndpoint endpoint, Recorder recorder)[] sides)
    {
        var clock = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(clock.Elapsed < _timeout, "timed out pumping GNS");
            foreach ((GnsEndpoint endpoint, Recorder recorder) in sides)
            {
                endpoint.Pump(recorder);
            }

            Thread.Sleep(1);
        }
    }

    [Fact]
    public void PortZeroListensOnARealPort()
    {
        GnsServer server = Track(GnsServer.Listen(Library(), IPAddress.Loopback, 0, 8));

        Assert.InRange(server.BoundPort, 1, ushort.MaxValue);
    }

    [Fact]
    public void ReliableMessagesArriveInOrder()
    {
        (GnsServer? server, Recorder? serverSide, GnsClient? client, Recorder? clientSide) = Connected();
        const int COUNT = 5_000;

        for (int i = 0; i < COUNT; i++)
        {
            Assert.Equal(GnsResult.OK, client.Send(BitConverter.GetBytes(i), GnsSend.RELIABLE | GnsSend.NO_NAGLE));
        }

        PumpUntil(() => serverSide.Messages.Count == COUNT, (server, serverSide), (client, clientSide));

        Assert.Equal(Enumerable.Range(0, COUNT), serverSide.Messages.Select(m => BitConverter.ToInt32(m.Data)));
    }

    [Fact]
    public void LingeringCloseDeliversTheLastReliableMessage()
    {
        (GnsServer? server, Recorder? serverSide, GnsClient? client, Recorder? clientSide) = Connected();
        uint peer = server.Connections.Single();

        server.Send(peer, [42], GnsSend.RELIABLE);
        server.Kick(peer, GnsEndReason.APP_KICKED, "bye", linger: true);
        PumpUntil(() => clientSide.Disconnected.Count == 1, (server, serverSide), (client, clientSide));

        Assert.Equal([42], clientSide.Messages.Single().Data);
        Assert.Equal(GnsEndReason.APP_KICKED, clientSide.Disconnected.Single().Reason);
    }

    [Fact]
    public void OversizedInboundMessageClosesTheConnection()
    {
        (GnsServer? server, Recorder? serverSide, GnsClient? client, Recorder? clientSide) = Connected(new GnsServerOptions { RecvMaxMessageBytes = 1024 });

        client.Send(new byte[2048], GnsSend.RELIABLE);
        PumpUntil(() => serverSide.Disconnected.Count == 1, (server, serverSide), (client, clientSide));

        Assert.Empty(serverSide.Messages);
        Assert.True(serverSide.Disconnected.Single().WasConnected);
    }

    [Fact]
    public void TerrainSizedBurstIsQueuedWhole()
    {
        (GnsServer? server, Recorder? serverSide, GnsClient? client, Recorder? clientSide) = Connected();
        uint peer = server.Connections.Single();
        // Mirrors a full terrain sync: 4 MB in 12 KB chunks sent in one loop.
        const int CHUNK = 12 * 1024;
        const int CHUNKS = 4 * 1024 * 1024 / CHUNK;

        for (int i = 0; i < CHUNKS; i++)
        {
            Assert.Equal(GnsResult.OK, server.Send(peer, new byte[CHUNK], GnsSend.RELIABLE | GnsSend.NO_NAGLE));
        }

        PumpUntil(() => clientSide.Messages.Count == CHUNKS, (server, serverSide), (client, clientSide));

        Assert.All(clientSide.Messages, m => Assert.Equal(CHUNK, m.Data.Length));
    }

    [Fact]
    public void NoNagleSkipsTheNagleDelay()
    {
        (GnsServer? server, Recorder? serverSide, GnsClient? client, Recorder? clientSide) = Connected();
        uint peer = server.Connections.Single();

        double MedianEcho(GnsSend flags)
        {
            var rounds = new List<double>();
            for (int i = 0; i < 15; i++)
            {
                int before = clientSide.Messages.Count;
                var clock = Stopwatch.StartNew();
                client.Send([1], flags);
                PumpUntil(() => serverSide.Messages.Count > before, (server, serverSide), (client, clientSide));
                server.Send(peer, [2], flags);
                PumpUntil(() => clientSide.Messages.Count > before, (server, serverSide), (client, clientSide));
                rounds.Add(clock.Elapsed.TotalMilliseconds);
            }
            return rounds.Order().ElementAt(rounds.Count / 2);
        }

        double nagle = MedianEcho(GnsSend.UNRELIABLE);
        double noNagle = MedianEcho(GnsSend.UNRELIABLE | GnsSend.NO_NAGLE);

        // Nagle holds each hop ~5 ms. Side by side, machine load cancels out.
        Assert.True(noNagle + 5 < nagle, $"no-Nagle echo {noNagle:F2} ms vs Nagle echo {nagle:F2} ms");
    }

    [Fact]
    public void GlobalFakeLagDelaysDelivery()
    {
        (GnsServer? server, Recorder? serverSide, GnsClient? client, Recorder? clientSide) = Connected();
        GnsLibrary lib = Library();
        lib.SetGlobalFakeNetwork(sendLagMs: 50, recvLagMs: 50, sendLossPercent: 0, recvLossPercent: 0);
        try
        {
            var clock = Stopwatch.StartNew();
            client.Send([1], GnsSend.RELIABLE | GnsSend.NO_NAGLE);
            PumpUntil(() => serverSide.Messages.Count == 1, (server, serverSide), (client, clientSide));

            // Send lag on the client plus receive lag on the server, both in this process.
            Assert.InRange(clock.Elapsed.TotalMilliseconds, 90, 5_000);
        }
        finally
        {
            lib.SetGlobalFakeNetwork(0, 0, 0, 0);
        }
    }

    [Fact]
    public void ConnectionsBeyondTheCapAreRefused()
    {
        GnsLibrary lib = Library();
        GnsServer server = Track(GnsServer.Listen(lib, IPAddress.Loopback, 0, maxConnections: 1));
        GnsClient first = Track(GnsClient.Connect(lib, "127.0.0.1", server.BoundPort));
        Recorder serverSide = new(), firstSide = new(), secondSide = new();
        PumpUntil(() => firstSide.Connected.Count == 1, (server, serverSide), (first, firstSide));

        GnsClient second = Track(GnsClient.Connect(lib, "127.0.0.1", server.BoundPort));
        PumpUntil(() => secondSide.Disconnected.Count == 1,
            (server, serverSide), (first, firstSide), (second, secondSide));

        Assert.False(secondSide.Disconnected.Single().WasConnected);
        Assert.Equal(GnsEndReason.APP_SERVER_FULL, secondSide.Disconnected.Single().Reason);
        Assert.Single(server.Connections);
    }

    [Fact]
    public void PeerDisconnectIsReported()
    {
        (GnsServer? server, Recorder? serverSide, GnsClient? client, Recorder? clientSide) = Connected();

        client.Dispose();
        PumpUntil(() => serverSide.Disconnected.Count == 1, (server, serverSide));

        Assert.Empty(server.Connections);
    }

    [Fact]
    public void StatusReportsAConnectedState()
    {
        (GnsServer? server, Recorder _, GnsClient _, Recorder _) = Connected();

        GnsRealTimeStatus? status = server.Status(server.Connections.Single());

        Assert.Equal(GnsState.CONNECTED, status?.State);
    }

    private sealed class Recorder : IGnsHandler
    {
        public readonly List<uint> Connected = [];
        public readonly List<(uint Connection, bool WasConnected, int Reason)> Disconnected = [];
        public readonly List<(uint Connection, byte[] Data)> Messages = [];

        void IGnsHandler.OnConnected(uint connection) => Connected.Add(connection);

        void IGnsHandler.OnDisconnected(uint connection, bool wasConnected, int reason, string debug) =>
            Disconnected.Add((connection, wasConnected, reason));

        void IGnsHandler.OnMessage(uint connection, ReadOnlySpan<byte> data) =>
            Messages.Add((connection, data.ToArray()));
    }
}
