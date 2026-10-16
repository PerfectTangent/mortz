using Mortz.Core.Identity;
using Mortz.Protocol.Net.Query;
using Mortz.Server.Platform;
using Mortz.Server.Players;
using Xunit;

namespace Mortz.Runtime.Tests.Server;

public class ServerPublicationTests
{
    private static ServerInfo Info(int players = 0) => new("Server", "deathmatch", "Arena", players, 8,
        true, true, 7777, 47, 1);

    [Fact]
    public void MixedPopulationOwnsOnlyGuestRecordsAndReleasesEachOnce()
    {
        Backend backend = new();
        ServerPublication publication = new(backend, true);
        Player guest = new(2, "Guest");
        Player verified = new(3, "Steam player", account: new VerifiedAccount(AccountProvider.STEAM, 42));
        publication.Advance(Info(2), [guest, verified], true, 0);
        publication.Advance(Info(2), [guest, verified], true, 1000);
        Assert.Equal(1, backend.Created);
        Assert.Contains((42UL, "Steam player"), backend.Updates);
        Assert.Contains((101UL, "Guest"), backend.Updates);
        publication.Advance(Info(1), [verified], true, 2000);
        publication.Dispose();
        publication.Dispose();
        Assert.Equal([101UL], backend.Ended);
        Assert.Null(guest.Account);
    }

    [Fact]
    public void PeerReuseAndCapabilityResetCreateFreshRecords()
    {
        Backend backend = new();
        using ServerPublication publication = new(backend, true);
        Player old = new(2, "Guest"), replacement = new(2, "Guest");
        publication.Advance(Info(1), [old], true, 0);
        publication.Advance(Info(1), [replacement], true, 1000);
        Assert.Equal([101UL], backend.Ended);
        publication.Advance(Info(1), [replacement], false, 1001);
        publication.Advance(Info(1), [replacement], false, 1002);
        Assert.Equal([101UL, 102UL], backend.Ended);
        publication.Advance(Info(1), [replacement], true, 2000);
        Assert.Equal(3, backend.Created);
    }

    [Fact]
    public void PartialCreationAndUpdateFailureDoNotLeakOrDuplicateRecords()
    {
        Backend backend = new() { FailCreate = true };
        ServerPublication publication = new(backend, true);
        Player guest = new(2, "Guest");
        publication.Advance(Info(1), [guest], true, 0);
        Assert.Empty(backend.Updates);
        Assert.Empty(backend.Ended);
        backend.FailCreate = false;
        backend.FailUpdate = true;
        publication.Advance(Info(1), [guest], true, 1000);
        Assert.Equal([102UL], backend.Ended);
        backend.FailUpdate = false;
        publication.Advance(Info(1), [guest], true, 2000);
        publication.Advance(Info(1), [guest], true, 3000);
        Assert.Equal(3, backend.Created);
        publication.Dispose();
        Assert.Equal([102UL, 103UL], backend.Ended);
    }

    private class Backend : IServerPublication
    {
        public int Created;
        public bool FailCreate;
        public bool FailUpdate;
        public List<ulong> Ended { get; } = [];
        public List<(ulong, string)> Updates { get; } = [];
        public void Publish(ServerInfo info) { }
        public void Advertise(bool active) { }
        public ulong CreateGuest() { Created++; return FailCreate ? 0 : (ulong)(100 + Created); }
        public bool Update(ulong accountId, string name) { Updates.Add((accountId, name)); return !FailUpdate; }
        public void EndGuest(ulong accountId) => Ended.Add(accountId);
    }
}
