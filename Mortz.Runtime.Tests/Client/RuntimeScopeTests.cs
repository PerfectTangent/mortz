using Mortz.Client.Match;
using Mortz.Client.Players;
using Mortz.Client.Session;
using Mortz.Client.Stats;
using Mortz.Core.Features;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Participation;
using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;
using Mortz.Core.Terrain;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Match;
using Mortz.Protocol.Net.Roster;
using Mortz.Protocol.Net.Stats;
using Mortz.Protocol.Replication;
using Xunit;

namespace Mortz.Runtime.Tests.Client;

public class RuntimeScopeTests
{
    private class Sender : IClientSender, ISessionExit
    {
        public void Send<T>(in T message) where T : struct, INetMessage<T> { }
        public void LeaveSession(string reason) { }
    }

    [Fact]
    public void ConnectionClosureStopsDeliveryAndRetiresPlayerState()
    {
        NetRouter router = new();
        Sender sender = new();
        ClientConnectionScope session = new(router, sender, () => 1, sender);
        PingUpdateMsg update = new([new PeerPing(1, 42)]);
        Assert.True(router.Dispatch(PingUpdateMsg.MsgId, PingUpdateMsg.Serialize(update)));
        ClientPlayer player = session.Features.Feature<ClientPlayers>().Find(1)!;
        Assert.Equal(42, session.Features.Feature<Pings>().Of(player));
        session.Dispose();
        Assert.False(router.Dispatch(PingUpdateMsg.MsgId, PingUpdateMsg.Serialize(update)));
        Assert.Throws<ObjectDisposedException>(() => session.Features.Feature<Pings>().Of(player));
    }

    [Fact]
    public void MatchClosureKeepsSessionWinsAndRetiresAllMatchState()
    {
        NetRouter router = new();
        Sender sender = new();
        using ClientConnectionScope session = new(router, sender, () => 1, sender);
        session.Features.Feature<SessionWins>().Handle(new SessionWinsMsg([new PeerWins(1, 3)]));
        ClientMatchRuntime match = session.OpenMatch(7, MatchParticipation.Active, EmptyTerrain(), new MatchConfig(), MapZones.None, 1, _ => { }, () => 0);
        ClientPlayer player = session.Features.Feature<ClientPlayers>().Find(1)!;
        Assert.NotNull(player.Match);
        session.CloseMatch();
        Assert.Null(player.Match);
        Assert.Equal(3, session.Features.Feature<SessionWins>().Of(player));
        Assert.False(router.Dispatch(MatchEndMsg.MsgId,
            MatchEndMsg.Serialize(new MatchEndMsg(false, 1, 0, [], MatchGeneration: 7))));
        Assert.Null(match.State.Winner);
    }

    [Fact]
    public void MatchOwnedCleanupRunsBeforeTheMatchCloses()
    {
        NetRouter router = new();
        Sender sender = new();
        using ClientConnectionScope session = new(router, sender, () => 1, sender);
        ClientMatchRuntime match = session.OpenMatch(7, MatchParticipation.Active, EmptyTerrain(),
            new MatchConfig(), MapZones.None, 1, _ => { }, () => 0);
        bool stateOpenDuringCleanup = false;
        session.OwnMatch(() => stateOpenDuringCleanup =
            match.State.TryApplyParticipation(7, MatchParticipation.JipSpectator));
        session.CloseMatch();
        Assert.True(stateOpenDuringCleanup);
        Assert.Null(session.Features.Child);
        Assert.False(match.State.TryApplyParticipation(7, MatchParticipation.Active));
    }

    [Fact]
    public void OwningMatchCleanupWithoutAnOpenMatchThrows()
    {
        NetRouter router = new();
        Sender sender = new();
        using ClientConnectionScope session = new(router, sender, () => 1, sender);
        Assert.Throws<InvalidOperationException>(() => session.OwnMatch(() => { }));
        session.OpenMatch(7, MatchParticipation.Active, EmptyTerrain(), new MatchConfig(),
            MapZones.None, 1, _ => { }, () => 0);
        session.CloseMatch();
        Assert.Throws<InvalidOperationException>(() => session.OwnMatch(() => { }));
    }

    [Fact]
    public void OldMatchPacketsAndReusedSlotsCannotMutateTheCurrentMatch()
    {
        NetRouter router = new();
        Sender sender = new();
        using ClientConnectionScope session = new(router, sender, () => 1, sender);
        ClientMatchRuntime match = session.OpenMatch(7, MatchParticipation.Active, EmptyTerrain(), new MatchConfig(), MapZones.None, 1, _ => { }, () => 0);
        session.Features.Feature<ClientPlayers>().Handle(new RosterMsg([new RosterEntry(1, "player", 0, null, 1)], Revision: 2));
        MatchSnapshot current = new(1,
            [new ReplicatedPlayer(new PlayerState { PeerId = 1, Health = 100 }, default)],
            Generation: 7, RosterRevision: 2);
        Assert.True(match.AcceptSnapshot(current.SerializeFor(1), -1));
        Assert.False(match.AcceptSnapshot((current with { Tick = 900, Generation = 6 }).SerializeFor(1), 800));
        Assert.False(match.AcceptSnapshot((current with { Tick = 901, RosterRevision = 1 }).SerializeFor(1), 800));
        MatchEndMsg stale = new(false, 1, 0, [], MatchGeneration: 6);
        Assert.False(router.Dispatch(MatchEndMsg.MsgId, MatchEndMsg.Serialize(stale)));
        Assert.Null(match.State.Winner);
        Assert.Equal(1, match.Interpolator.NewestTick);
    }

    [Fact]
    public void AdmissionAtTheBootstrapTickAddsTheLocalSpawnOnce()
    {
        NetRouter router = new();
        Sender sender = new();
        using ClientConnectionScope session = new(router, sender, () => 1, sender);
        ClientMatchRuntime match = session.OpenMatch(7, MatchParticipation.Active, EmptyTerrain(), new MatchConfig(), MapZones.None, 1, _ => { }, () => 0);
        Assert.True(match.InitializeSnapshot(new MatchSnapshot(10, [], 7, 1).Serialize(), -1));
        Assert.False(match.Predictor.Initialized);
        match.Handle(new MatchStartMsg(7));
        match.Tick(new PlayerInput(InputButtons.RIGHT));
        Assert.Equal(0, match.Predictor.NextSeq);
        session.Features.Feature<ClientPlayers>().Handle(new RosterMsg([new RosterEntry(1, "player", 0, null, 1)], Revision: 2));
        MatchSnapshot admitted = new(10,
            [new ReplicatedPlayer(new PlayerState { PeerId = 1, Health = 100, Position = new Vec2(12, 12) }, default)],
            Generation: 7, RosterRevision: 2);
        Assert.True(match.AcceptSnapshot(admitted.SerializeFor(1), -1));
        Assert.True(match.Predictor.Initialized);
        Assert.Single(match.Interpolator.Advance(0)!.Players);
        Assert.False(match.AcceptSnapshot(admitted.SerializeFor(1), -1));
        Assert.False(match.AcceptSnapshot((admitted with { RosterRevision = 1 }).SerializeFor(1), -1));
    }

    [Fact]
    public void ScopeRejectsDuplicateRegistration()
    {
        using FeatureScope scope = new("connection", _ => { }, _ => { });
        object feature = scope.Register(new object());
        Assert.Throws<InvalidOperationException>(() => scope.Register(feature));
    }

    [Fact]
    public void InitialSnapshotSeedsTheMatchBeforeItsRosterAndCannotBeReapplied()
    {
        NetRouter router = new();
        Sender sender = new();
        using ClientConnectionScope session = new(router, sender, () => 1, sender);
        ClientMatchRuntime match = session.OpenMatch(7, MatchParticipation.Active, EmptyTerrain(), new MatchConfig(), MapZones.None, 1, _ => { }, () => 0);
        MatchSnapshot initial = new(0,
            [new ReplicatedPlayer(new PlayerState { PeerId = 1, Health = 100 }, default),
             new ReplicatedPlayer(new PlayerState { PeerId = 2, Health = 100 }, default)],
            Generation: 7, RosterRevision: 1);

        Assert.False(match.InitializeSnapshot(initial.SerializeFor(1), -1));
        Assert.False(match.InitializeSnapshot((initial with { Generation = 6 }).Serialize(), -1));
        Assert.True(match.InitializeSnapshot(initial.Serialize(), -1));
        Assert.Equal(0, match.Interpolator.NewestTick);
        Assert.NotNull(session.Features.Feature<ClientPlayers>().Find(2)?.Match);
        Assert.False(match.InitializeSnapshot((initial with { Tick = 100 }).Serialize(), -1));

        MatchSnapshot live = initial with { Tick = 2 };
        Assert.False(match.AcceptSnapshot(live.SerializeFor(1), -1));
        session.Features.Feature<ClientPlayers>().Handle(new RosterMsg(
            [new RosterEntry(1, "local", 0, null, 1), new RosterEntry(2, "remote", 0, null, 2)], Revision: 1));
        Assert.True(match.AcceptSnapshot(live.SerializeFor(1), -1));
        Assert.False(match.AcceptSnapshot([.. live.SerializeFor(1), 0], -1));
        Assert.False(match.AcceptSnapshot([0], -1));
        Assert.Equal(2, match.Interpolator.NewestTick);
    }

    private static TerrainMask EmptyTerrain() => new(64, 64, (_, _) => false, (_, _) => false);
}
