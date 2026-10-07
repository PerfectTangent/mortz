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
    private static readonly ObjectiveWire _wire = ObjectiveWire.Create(new DeadlockObjectiveRules().ToSnapshot());
    private static readonly DeadlockObjectiveState _live = new(6000, 2650.5m, 1000.25m, true, false, 0, 90, false, false, null);
    private static readonly DeadlockObjectiveState _captured = new(6000, 6000, 0, true, false, 0, 0, false, true, Team.BLUE);

    [Fact]
    public void NoObjectiveWritesNoPayloadAndDeadlockPreservesExactFractionalState()
    {
        Assert.Empty(ObjectiveWire.None.Serialize(NoObjectiveState.Instance));
        MatchSnapshot plain = new(12, [], 7);
        MatchSnapshot objective = plain with { Objective = _live };
        byte[] bytes = objective.Serialize(_wire);
        Assert.Equal(plain.Serialize().Length + 38, bytes.Length);
        Assert.Equal(_live, MatchSnapshot.Deserialize(bytes, _wire).Objective);
        Assert.Equal(_live, MatchSnapshot.Deserialize(objective.SerializeFor(1, _wire), _wire).Objective);
        Assert.Equal(_live, _wire.Deserialize(_wire.Serialize(_live)));
        Assert.Throws<InvalidDataException>(() => MatchSnapshot.Deserialize(bytes));
        Assert.Throws<EndOfStreamException>(() => MatchSnapshot.Deserialize(plain.Serialize(), _wire));
        Assert.Throws<InvalidDataException>(() => objective.Serialize());
        Assert.Throws<InvalidDataException>(() => _wire.Serialize(NoObjectiveState.Instance));
    }

    [Fact]
    public void TruncatedAndTrailingBytesAreRejected()
    {
        byte[] corruptDecimal = _wire.Serialize(_live);
        corruptDecimal[12] = 1;
        Assert.Throws<IOException>(() => _wire.Deserialize(corruptDecimal));
        Assert.Throws<InvalidDataException>(() => _wire.Deserialize([.. _wire.Serialize(_live), 0]));
        Assert.Throws<EndOfStreamException>(() => _wire.Deserialize(_wire.Serialize(_live)[..^1]));
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
        Assert.True(runtime.InitializeSnapshot(initial.Serialize(_wire), -1));
        Assert.Equal(_live, state.Objective);
        Assert.False(runtime.AcceptSnapshot((initial with { Tick = 9 }).Serialize(_wire), -1));
        Assert.False(runtime.AcceptSnapshot((initial with { Generation = 6, Tick = 100 }).Serialize(_wire), -1));
        MatchSnapshot final = initial with { Tick = 61, Objective = _captured };
        if (liveFinalFirst)
        {
            Assert.True(runtime.AcceptSnapshot(final.Serialize(_wire), -1));
        }
        int winners = 0;
        state.WinnerChanged += winner =>
        {
            winners++;
            Assert.Equal(_captured, state.Objective);
            Assert.Equal(61, state.ObjectiveTick);
            Assert.Equal(new Victor.Team(Team.BLUE), winner);
        };
        MatchEndMsg result = new(true, (int)Team.BLUE, 61, _wire.Serialize(_captured), 7);
        Assert.True(router.Dispatch(MatchEndMsg.MsgId, MatchEndMsg.Serialize(result)));
        router.Dispatch(MatchEndMsg.MsgId, MatchEndMsg.Serialize(result));
        Assert.Equal(1, winners);
        Assert.Equal(!liveFinalFirst, runtime.AcceptSnapshot(final.Serialize(_wire), -1));
        Assert.True(runtime.AcceptSnapshot((initial with { Tick = 62 }).Serialize(_wire), -1));
        Assert.Equal(_captured, state.Objective);
    }

    [Fact]
    public void AWinnerWithoutACaptureKeepsTheLiveObjective()
    {
        ClientMatchState state = new(7, MatchParticipation.Active);
        ClientMatchStateAdapter adapter = new(state, _wire);
        adapter.Handle(new MatchEndMsg(true, 1, 61, _wire.Serialize(_captured), 6));
        Assert.Null(state.Winner);

        adapter.Handle(new MatchEndMsg(true, 2, 61, _wire.Serialize(_live), 7));

        Assert.Equal(new Victor.Team(Team.RED), state.Winner);
        Assert.Equal(_live, state.Objective);
        Assert.Equal(61, state.ObjectiveTick);
    }

    [Fact]
    public void AClosedMatchIgnoresItsResult()
    {
        ClientMatchState state = new(7, MatchParticipation.Active);
        ClientMatchStateAdapter adapter = new(state, _wire);
        state.Close();
        adapter.Handle(new MatchEndMsg(true, 1, 61, _wire.Serialize(_captured), 7));
        Assert.Null(state.Winner);
    }
}
