using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Admission;
using Mortz.Server.Admission;
using Xunit;

namespace Mortz.Runtime.Tests.Server;

public class AdmissionTests
{
    [Fact]
    public void GuestAcceptancePrecedesGameplay()
    {
        using Fixture f = new();
        f.Join(2);
        Assert.Equal(new[] { "offer:GUEST", "accepted", "gameplay" }, f.Order);
        Assert.Equal(AdmissionMode.GUEST, Assert.Single(f.Offers).Mode);
        Assert.Equal(0UL, Assert.Single(f.Offers).ServerAccountId);
        Assert.Equal(1, f.Admission.AdmittedCount);
    }

    [Fact]
    public void AdmittedPlayerCarriesTheHelloProfile()
    {
        using Fixture f = new();
        f.Admission.Connected(2, 0);
        f.Admission.Hello(2, Fixture.Hello with { Name = "alice", Skin = 1 }, 0);
        Assert.Equal(new AdmittedPlayer(2, "alice", 1), Assert.Single(f.Players));
    }

    [Fact]
    public void SteamHelloIsStillAdmittedAsGuest()
    {
        using Fixture f = new();
        f.Admission.Connected(2, 0);
        f.Admission.Hello(2, Fixture.Hello with { Steam = true }, 0);
        Assert.Equal(AdmissionMode.GUEST, Assert.Single(f.Offers).Mode);
        Assert.Single(f.Players);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProofIsAlwaysOutOfTurn(bool admitted)
    {
        using Fixture f = new();
        f.Admission.Connected(2, 0);
        if (admitted)
            f.Admission.Hello(2, Fixture.Hello, 0);
        f.Admission.Proof(2);
        Assert.Equal(AdmissionRejection.INVALID_STATE, Assert.Single(f.Rejections).Reason);
        Assert.Equal(admitted ? new[] { 2 } : [], f.Departed);
        Assert.Equal(0, f.Admission.AdmittedCount);
    }

    [Fact]
    public void DuplicateHelloDoesNotReopenAnAttempt()
    {
        using Fixture f = new();
        f.Join(2);
        f.Admission.Hello(2, Fixture.Hello, f.Now);
        Assert.Equal(AdmissionRejection.INVALID_STATE, Assert.Single(f.Rejections).Reason);
        Assert.Single(f.Offers);
        Assert.Equal(new[] { 2 }, f.Departed);
    }

    [Fact]
    public void DuplicateConnectIsRejected()
    {
        using Fixture f = new();
        f.Admission.Connected(2, 0);
        f.Admission.Connected(2, 0);
        Assert.Equal(AdmissionRejection.INVALID_STATE, Assert.Single(f.Rejections).Reason);
    }

    [Fact]
    public void HelloDeadlineExpiresExactlyAtBoundary()
    {
        using Fixture f = new();
        f.Admission.Connected(2, 0);
        f.Admission.Advance(AdmissionLimits.HELLO_MS - 1);
        Assert.Empty(f.Rejections);
        f.Admission.Advance(AdmissionLimits.HELLO_MS);
        Assert.Equal(AdmissionRejection.HELLO_TIMEOUT, Assert.Single(f.Rejections).Reason);
    }

    [Fact]
    public void LateHelloCannotBeatDeadlineTick()
    {
        using Fixture f = new();
        f.Admission.Connected(2, 0);
        f.Admission.Hello(2, Fixture.Hello, AdmissionLimits.HELLO_MS);
        Assert.Equal(AdmissionRejection.HELLO_TIMEOUT, Assert.Single(f.Rejections).Reason);
        Assert.Empty(f.Players);
    }

    [Fact]
    public void AdmittedPlayersNeverTimeOut()
    {
        using Fixture f = new();
        f.Join(2);
        f.Admission.Advance(ulong.MaxValue);
        Assert.Empty(f.Rejections);
        Assert.Equal(1, f.Admission.AdmittedCount);
    }

    [Fact]
    public void AdmittedSlotsCannotExceedCapacity()
    {
        using Fixture f = new();
        for (int peer = 2; peer < AdmissionLimits.RESERVATIONS + 2; peer++)
        {
            f.Join(peer);
        }
        f.Join(100);
        Assert.Equal(AdmissionRejection.SERVER_FULL, Assert.Single(f.Rejections).Reason);
        Assert.Equal(AdmissionLimits.RESERVATIONS, f.Admission.AdmittedCount);
        f.Admission.Disconnected(3);
        f.Join(101);
        Assert.Equal(AdmissionLimits.RESERVATIONS, f.Admission.AdmittedCount);
    }

    [Fact]
    public void OnlyAdmittedPlayersDepart()
    {
        using Fixture f = new();
        f.Admission.Connected(2, 0);
        f.Join(3);
        f.Admission.Disconnected(2);
        f.Admission.Disconnected(3);
        Assert.Equal(new[] { 3 }, f.Departed);
    }

    [Fact]
    public void DisposeIsIdempotentAndStopsNewAdmission()
    {
        Fixture f = new();
        f.Join(2);
        f.Admission.Dispose();
        f.Admission.Dispose();
        f.Admission.Connected(3, 0);
        Assert.Equal(new[] { 2 }, f.Departed);
        Assert.Equal(0, f.Admission.AdmittedCount);
        Assert.Equal(2, f.Rejections.Count);
        Assert.All(f.Rejections, item => Assert.Equal(AdmissionRejection.SERVER_STOPPED, item.Reason));
    }

    [Theory]
    [InlineData(1, 0UL)]
    [InlineData(47, 0UL)]
    public void IncompatibleHelloIsTerminal(int protocol, ulong schema)
    {
        using Fixture f = new();
        f.Admission.Connected(2, 0);
        f.Admission.Hello(2, new AdmissionHello(protocol, schema, "name", 0, true), 0);
        Assert.Equal(AdmissionRejection.INCOMPATIBLE, Assert.Single(f.Rejections).Reason);
        Assert.Empty(f.Offers);
    }

    [Theory]
    [InlineData(-1, "name")]
    [InlineData(999, "name")]
    [InlineData(0, "1234567890123456789012345")]
    [InlineData(0, null)]
    public void InvalidProfileIsRejectedBeforeAdmission(int skin, string? name)
    {
        using Fixture f = new();
        f.Admission.Connected(2, 0);
        f.Admission.Hello(2, Fixture.Hello with { Skin = skin, Name = name! }, 0);
        Assert.Equal(AdmissionRejection.INVALID_PROFILE, Assert.Single(f.Rejections).Reason);
        Assert.Equal(0, f.Admission.AdmittedCount);
    }

    public class RecordingTransport : IServerAdmissionTransport
    {
        public readonly List<string> Order = [];
        public readonly List<AdmissionOffer> Offers = [];
        public readonly List<AdmissionRejected> Rejections = [];
        public void Offer(int peerId, AdmissionOffer offer) { Offers.Add(offer); Order.Add($"offer:{offer.Mode}"); }
        public void Accept(int peerId, AdmissionAccepted accepted) => Order.Add("accepted");
        public void Reject(int peerId, AdmissionRejected rejected) => Rejections.Add(rejected);
    }

    public class Fixture : RecordingTransport, IDisposable
    {
        public static readonly AdmissionHello Hello = new(NetConfig.PROTOCOL_VERSION, NetRegistry.SCHEMA_HASH, "player", 0, false);
        public readonly ServerAdmission Admission;
        public readonly List<AdmittedPlayer> Players = [];
        public readonly List<int> Departed = [];
        public ulong Now;
        public Fixture()
        {
            Admission = new ServerAdmission(this);
            Admission.Admitted += player => { Players.Add(player); Order.Add("gameplay"); };
            Admission.Departed += Departed.Add;
        }
        public void Join(int peerId) { Admission.Connected(peerId, Now); Admission.Hello(peerId, Hello, Now); }
        public void Dispose() => Admission.Dispose();
    }
}
