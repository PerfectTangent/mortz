using Mortz.Core.Features;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Objectives;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Match;
using Mortz.Protocol.Replication;
using Mortz.Runtime.Tests.Core;
using Mortz.Server;
using Mortz.Server.Content;
using Mortz.Server.Hooks;
using Mortz.Server.Match;
using Mortz.Server.Match.Services;
using Mortz.Server.Phases;
using Mortz.Server.Players;
using Serilog.Core;
using Xunit;

namespace Mortz.Runtime.Tests.Server.Match;

public class DeadlockReplicationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BootstrapAndReliableResultsReachPlayersAndSpectators(bool spectatorLoading)
    {
        MatchConfig config = DeadlockMatchTests.Config();
        ((DeadlockObjectiveRules)config.Rules.Objective).CaptureSpeed = 99;
        ObjectiveWire objectiveWire = ObjectiveWire.Create(config.Rules.Objective.ToSnapshot());
        MapSnapshot map = TestServer.Map("control", "Control") with
        {
            InitialTerrain = TestWorlds.Flat(),
            Zones = DeadlockMatchTests.Zones(),
        };
        FeatureScope scope = new("match");
        Roster roster = new();
        RecordingTransport wire = new();
        ReadyLink link = new(wire);
        MatchRuntime runtime = new(map.BuildMask(), config, 2, scope, 3, zones: map.Zones);
        for (int id = 1; id <= 3; id++)
        {
            Player player = roster.Join(id, $"P{id}");
            if (id < 3) runtime.Seat(player, id == 1 ? Team.BLUE : Team.RED);
            else runtime.AddJipSpectator(player);
            link.BeginLoading(id, 3, 0);
            if (id < 3 || !spectatorLoading)
            {
                link.Ready(id, 3);
            }
        }
        runtime.World.Teleport(1, new Vec2(50, 250));
        runtime.World.Teleport(2, new Vec2(200, 250));
        MatchReplication replication = new(runtime, roster, map, new TerrainHistory(), link, Logger.None, false);
        replication.RosterChanged();
        replication.Load(roster.Find(3)!, new PhaseLoad(ServerPhaseKind.MATCH, 3, Initial: true));
        MatchLoadMsg bootstrap = wire.Last<MatchLoadMsg>();
        Assert.IsType<DeadlockObjectiveState>(MatchSnapshot.Deserialize(bootstrap.InitialSnapshot, objectiveWire).Objective);
        wire.Messages.Clear();
        for (int tick = 0; tick < 61; tick++)
        {
            replication.MatchUpdated(runtime.Advance(default));
        }
        Assert.NotEqual(0, runtime.World.Tick % NetConfig.TICKS_PER_SNAPSHOT);
        if (spectatorLoading)
        {
            Assert.DoesNotContain(wire.Messages, sent => sent.Target == 3 && sent.Message is MatchEndMsg);
            Assert.True(link.Ready(3, 3));
        }
        Sent[] results = wire.Messages.Where(sent => sent.Message is MatchEndMsg).ToArray();
        Assert.Equal(spectatorLoading ? [1, 2, 3] : new[] { 0 }, results.Select(sent => sent.Target));
        foreach (Sent sent in results)
        {
            MatchEndMsg result = (MatchEndMsg)sent.Message;
            Assert.Equal(61, result.Tick);
            Assert.Equal(3, result.MatchGeneration);
            Assert.Equal(runtime.Objective, objectiveWire.Deserialize(result.Objective));
        }
    }

    [Fact]
    public void SpectatorsKeepReceivingObjectiveDecayAfterTheLastPlayerLeaves()
    {
        MatchConfig config = DeadlockMatchTests.Config();
        ObjectiveWire objectiveWire = ObjectiveWire.Create(config.Rules.Objective.ToSnapshot());
        MapSnapshot map = TestServer.Map("control", "Control") with
        {
            InitialTerrain = TestWorlds.Flat(),
            Zones = DeadlockMatchTests.Zones(),
        };
        FeatureScope scope = new("match");
        Roster roster = new();
        RecordingTransport wire = new();
        ReadyLink link = new(wire);
        MatchRuntime runtime = new(map.BuildMask(), config, 2, scope, 3, zones: map.Zones);
        Player blue = roster.Join(1, "Blue");
        Player spectator = roster.Join(2, "Spectator");
        foreach (Player player in roster)
        {
            link.BeginLoading(player.PeerId, 3, 0);
            link.Ready(player.PeerId, 3);
        }
        runtime.Seat(blue, Team.BLUE);
        runtime.AddJipSpectator(spectator);
        runtime.World.Teleport(1, new Vec2(50, 250));
        MatchReplication replication = new(runtime, roster, map, new TerrainHistory(), link, Logger.None, false);
        replication.RosterChanged();
        for (int tick = 0; tick < 4; tick++)
        {
            replication.MatchUpdated(runtime.Advance(default));
        }
        Assert.Equal(400, Assert.IsType<DeadlockObjectiveState>(runtime.Objective).BlueProgress);
        runtime.Remove(blue);
        roster.Leave(blue.PeerId);
        link.Remove(blue.PeerId);
        replication.RosterChanged();
        wire.Snapshots.Clear();

        for (int tick = 0; tick < NetConfig.TICKS_PER_SNAPSHOT; tick++)
        {
            replication.MatchUpdated(runtime.Advance(default));
        }

        SentSnapshot sent = Assert.Single(wire.Snapshots);
        Assert.Equal(spectator.PeerId, sent.Target);
        MatchSnapshot snapshot = MatchSnapshot.Deserialize(sent.Data, objectiveWire);
        Assert.Empty(snapshot.Players);
        Assert.Equal(runtime.World.Tick, snapshot.Tick);
        DeadlockObjectiveState objective = Assert.IsType<DeadlockObjectiveState>(snapshot.Objective);
        Assert.Equal(200, objective.BlueProgress);
        Assert.True(objective.BlueDecaying);
        Assert.False(objective.BlueOccupied);
    }
}
