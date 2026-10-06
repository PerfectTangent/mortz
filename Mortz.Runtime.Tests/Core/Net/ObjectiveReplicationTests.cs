using Mortz.Client.Match;
using Mortz.Client.Players;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Objectives;
using Mortz.Core.Match.Participation;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim.Modifiers;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Match;
using Mortz.Protocol.Replication;
using Mortz.Runtime.Tests.Server.Match;
using Xunit;

namespace Mortz.Runtime.Tests.Core.Net;

public class ObjectiveReplicationTests
{
    private static readonly ObjectiveSnapshotCodec _codec = ObjectiveSnapshotCodec.Create(new DeadlockObjectiveRules().ToSnapshot());
    private static readonly DeadlockObjectiveState _live = new(6000, 2650.5m, 1000.25m, true, false, 0, 90, false, false, null);
    private static readonly DeadlockObjectiveState _captured = new(6000, 6000, 0, true, false, 0, 0, false, true, Team.BLUE);

    [Fact]
    public void NoObjectiveWritesNoPayloadAndDeadlockPreservesExactFractionalState()
    {
        Assert.Empty(ObjectiveSnapshotCodec.None.Serialize(NoObjectiveState.Instance));
        MatchSnapshot plain = new(12, [], 7);
        MatchSnapshot objective = plain with { Objective = _live };
        byte[] bytes = objective.Serialize(_codec);
        Assert.Equal(plain.Serialize().Length + 38, bytes.Length);
        Assert.Equal(_live, MatchSnapshot.Deserialize(bytes, _codec).Objective);
        Assert.Equal(_live, MatchSnapshot.Deserialize(objective.SerializeFor(1, _codec), _codec).Objective);
        Assert.Equal(_live, _codec.Deserialize(_codec.Serialize(_live)));
        Assert.Throws<InvalidDataException>(() => MatchSnapshot.Deserialize(bytes));
        Assert.Throws<EndOfStreamException>(() => MatchSnapshot.Deserialize(plain.Serialize(), _codec));
        Assert.Throws<InvalidDataException>(() => objective.Serialize());
        Assert.Throws<InvalidDataException>(() => _codec.Serialize(NoObjectiveState.Instance));
    }

    [Fact]
    public void MalformedProgressFlagsGraceAndTrailingBytesAreRejected()
    {
        foreach (DeadlockObjectiveState invalid in new[]
        {
            _live with { BlueProgress = -1 }, _live with { RedProgress = 5000 },
            _live with { BlueDecaying = true }, _live with { RedGraceTicks = 121 },
            _live with { Capture = Team.BLUE }, _captured with { Capture = null },
        })
        {
            Assert.Throws<InvalidDataException>(() => _codec.Serialize(invalid));
        }
        byte[] flags = _codec.Serialize(_live);
        flags[32] = 128;
        Assert.Throws<InvalidDataException>(() => _codec.Deserialize(flags));
        byte[] corruptDecimal = _codec.Serialize(_live);
        corruptDecimal[12] = 1;
        Assert.Throws<IOException>(() => _codec.Deserialize(corruptDecimal));
        Assert.Throws<InvalidDataException>(() => _codec.Deserialize([.. _codec.Serialize(_live), 0]));
        Assert.Throws<EndOfStreamException>(() => _codec.Deserialize(_codec.Serialize(_live)[..^1]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReliableCompletionSurvivesLossReorderingAndSameTickSnapshots(bool liveFinalFirst)
    {
        MatchConfig config = DeadlockMatchTests.Config();
        ((DeadlockObjectiveRules)config.Rules.Objective).AbsenceDelay = 2;
        NetRouter router = new() { MatchGeneration = 7 };
        ClientMatchState state = new(7, MatchParticipation.JipSpectator);
        using ClientMatchRuntime runtime = new(state, new ClientPlayers(), TestWorlds.Flat(), config,
            MapZones.None, 3, router, _ => { }, () => 0);
        MatchSnapshot initial = new(10, [], 7) { Objective = _live };
        Assert.True(runtime.InitializeSnapshot(initial.Serialize(_codec), -1));
        Assert.Equal(_live, state.Objective);
        Assert.False(runtime.AcceptSnapshot((initial with { Tick = 9 }).Serialize(_codec), -1));
        Assert.False(runtime.AcceptSnapshot((initial with { Generation = 6, Tick = 100 }).Serialize(_codec), -1));
        MatchSnapshot final = initial with { Tick = 61, Objective = _captured };
        if (liveFinalFirst)
        {
            Assert.True(runtime.AcceptSnapshot(final.Serialize(_codec), -1));
        }
        int winners = 0;
        state.WinnerChanged += winner =>
        {
            winners++;
            Assert.Equal(_captured, state.Objective);
            Assert.Equal(61, state.ObjectiveTick);
            Assert.Equal(new Victor.Team(Team.BLUE), winner);
        };
        ObjectiveMatchEndMsg result = new(true, (int)Team.BLUE, 61, _codec.Serialize(_captured), 7);
        Assert.True(router.Dispatch(ObjectiveMatchEndMsg.MsgId, ObjectiveMatchEndMsg.Serialize(result)));
        router.Dispatch(ObjectiveMatchEndMsg.MsgId, ObjectiveMatchEndMsg.Serialize(result));
        Assert.Equal(1, winners);
        Assert.Equal(!liveFinalFirst, runtime.AcceptSnapshot(final.Serialize(_codec), -1));
        Assert.True(runtime.AcceptSnapshot((initial with { Tick = 62 }).Serialize(_codec), -1));
        Assert.Equal(_captured, state.Objective);
    }

    [Fact]
    public void MissingOrInvalidFinalStateCannotPublishAWinner()
    {
        ClientMatchState state = new(7, MatchParticipation.Active);
        ClientMatchStateAdapter adapter = new(state, _codec);
        adapter.Handle(new MatchEndMsg(true, 1, 7));
        adapter.Handle(new ObjectiveMatchEndMsg(true, 1, 61, [], 7));
        adapter.Handle(new ObjectiveMatchEndMsg(true, 2, 61, _codec.Serialize(_captured), 7));
        adapter.Handle(new ObjectiveMatchEndMsg(true, 1, 61, _codec.Serialize(_captured), 6));
        adapter.Handle(new ObjectiveMatchEndMsg(true, 1, 61, _codec.Serialize(_live), 7));
        Assert.Null(state.Winner);
        state.Close();
        adapter.Handle(new ObjectiveMatchEndMsg(true, 1, 61, _codec.Serialize(_captured), 7));
        Assert.Null(state.Winner);
    }
}
