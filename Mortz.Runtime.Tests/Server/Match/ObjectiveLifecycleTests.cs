using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Objectives;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Server.Match;
using Mortz.Server.Match.Modes;
using Mortz.Server.Players;
using Xunit;

namespace Mortz.Runtime.Tests.Server.Match;

public class ObjectiveLifecycleTests
{
    [Fact]
    public void CompletionWinsForTheCapturingTeamAfterDeathsAndRespawnsIndependentlyOfKills()
    {
        SampleObjective objective = new();
        MatchStateKeys keys = new(1);
        using MatchRuntime runtime = new(new TerrainMask(128, 128, (_, y) => y >= 96, (_, _) => false),
            new MatchConfig
            {
                Rules = new ModeRules
                {
                    Teams = true,
                    SpawnImmunity = 0,
                    Respawn = new FixedRespawnRules { Delay = 0 },
                    SuicidePenalty = SuicidePenalty.REWARD_CLOSEST_ENEMY,
                }
            }, 2, keys,
            [new SpawnPoint(new Vec2(16, 96)), new SpawnPoint(new Vec2(64, 96))],
            mode: new GameMode(new KillScores(), new ObjectiveCompletionCondition(new()),
                new QualifyingWinner(), EvaluationTiming.TICK, new DecisiveEliminationReplay(), objective));
        keys.Seal();
        Player blue = OpenPlayer(1, keys);
        Player red = OpenPlayer(2, keys);
        runtime.Seat(blue, Team.BLUE);
        runtime.Seat(red, Team.RED);
        runtime.World.QueueDamage(1, byte.MaxValue);
        MatchUpdate first = runtime.Advance(default);
        Assert.Equal(1, runtime.TeamKills.Red);
        Assert.Null(first.Standing.Leader);
        Assert.Equal(-1, first.Standing.Remaining);
        Assert.False(objective.Samples[0].BlueAlive);

        runtime.Advance(default);
        runtime.World.QueueDamage(1, byte.MaxValue);
        runtime.Advance(default);
        runtime.World.QueueDamage(2, byte.MaxValue);
        objective.Capture = Team.BLUE;
        MatchUpdate ended = runtime.Advance(default);

        Assert.Equal((4, true, false), objective.Samples[3]);
        Assert.True(runtime.TeamKills.Blue < runtime.TeamKills.Red);
        Assert.Equal(new Victor.Team(Team.BLUE), ended.MatchEnded);
        Assert.Equal(ended.MatchEnded, ended.Standing.Leader);
        Assert.Equal(-1, ended.Standing.Remaining);
        Assert.Null(ended.FinalKill);
        Assert.Single(ended.Eliminations);
        ObjectiveState captured = runtime.Objective;
        objective.Capture = Team.RED;
        runtime.Advance(default);
        Assert.True(runtime.Advance(default).ReturnToLobby);
        Assert.Equal(4, objective.Samples.Count);
        Assert.Equal(4, objective.AdvanceCalls);
        Assert.Same(captured, runtime.Objective);
        Assert.Throws<InvalidOperationException>(() => runtime.Seat(OpenPlayer(3, keys)));
        Assert.Throws<InvalidOperationException>(() => runtime.AddJipSpectator(OpenPlayer(4, keys)));
    }

    [Fact]
    public void CompletedObjectiveIsFrozenEvenIfTheEndConditionKeepsTheMatchPlaying()
    {
        SampleObjective objective = new() { Capture = Team.BLUE };
        using MatchRuntime runtime = new(new TerrainMask(8, 8, (_, _) => false, (_, _) => false),
            new MatchConfig(), 2, new MatchStateKeys(1), mode: GameMode.Create(new ModeRules().ToSnapshot(), objective));
        Assert.Null(runtime.Advance(default).MatchEnded);
        objective.Capture = Team.RED;
        runtime.Advance(default);
        Assert.Equal(Team.BLUE, runtime.Objective.CapturedBy);
        Assert.Single(objective.Samples);
        Assert.Equal(2, objective.AdvanceCalls);
        Assert.Equal(MatchStage.PLAYING, runtime.Stage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedEvaluationReadsAnImmutableResultWithoutUpdatingTheObjective(bool completed)
    {
        SampleObjective objective = new() { Capture = completed ? Team.RED : null };
        GameMode mode = new(new KillScores(), EndCondition.Create(new ObjectiveCompletionRules()),
            WinnerSelections.Create(WinnerRule.QUALIFYING), EvaluationTiming.TICK, new NoReplay(), objective);
        using MatchRuntime runtime = new(new TerrainMask(8, 8, (_, _) => false, (_, _) => false),
            new MatchConfig(), 2, new MatchStateKeys(1), mode: mode);
        runtime.Advance(default);
        GameModeContext context = new(runtime.World, [], default, objective: runtime.Objective);
        Victor? expected = completed ? new Victor.Team(Team.RED) : null;
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(expected, mode.Evaluate(context)?.Winner);
            Assert.Equal(expected, mode.Standing(context).Leader);
        }
        Assert.Same(mode.Objective, runtime.Objective);
        Assert.Single(objective.Samples);
        Assert.Equal(1, objective.AdvanceCalls);
    }

    [Theory]
    [InlineData(EvaluationTiming.TICK)]
    [InlineData(EvaluationTiming.ELIMINATION)]
    public void MultipleDeathsStillAdvanceTheObjectiveOnlyOncePerTick(EvaluationTiming timing)
    {
        SampleObjective objective = new();
        MatchConfig config = new()
        {
            Rules = new ModeRules
            {
                SpawnImmunity = 0,
                Score = ScoreMetric.DEATHS,
                Evaluation = timing,
                EndCondition = new ScoreTargetRules { Target = 999 },
            }
        };
        MatchStateKeys keys = new(1);
        using MatchRuntime runtime = new(new TerrainMask(128, 128, (_, y) => y >= 96, (_, _) => false),
            config, 2, keys, mode: GameMode.Create(config.Rules.ToSnapshot(), objective));
        keys.Seal();
        runtime.Seat(OpenPlayer(1, keys));
        runtime.Seat(OpenPlayer(2, keys));
        runtime.World.QueueDamage(1, byte.MaxValue);
        runtime.World.QueueDamage(2, byte.MaxValue);

        MatchUpdate update = runtime.Advance(default);

        Assert.Equal(2, update.Eliminations.Length);
        Assert.Equal((1, false, false), Assert.Single(objective.Samples));
        Assert.Null(update.MatchEnded);
        runtime.Advance(default);
        Assert.Equal(2, objective.Samples.Count);
    }

    [Fact]
    public void NoObjectiveHasNoResultOrChangingState()
    {
        NoObjective objective = new();
        using MatchRuntime runtime = new(new TerrainMask(8, 8, (_, _) => false, (_, _) => false),
            new MatchConfig(), 0, new MatchStateKeys(1));
        runtime.Advance(default);
        runtime.Advance(default);
        Assert.Same(NoObjectiveState.Instance, runtime.Objective);
        Assert.Null(runtime.Objective.CapturedBy);
        Assert.Same(objective.State, runtime.Objective);
    }

    private static Player OpenPlayer(int id, MatchStateKeys keys)
    {
        Player player = new(id, $"Player {id}", 0, 1);
        player.OpenMatch(keys.Count, keys.Generation);
        return player;
    }

    private record CaptureState(Team? Team) : ObjectiveState
    {
        public override Team? CapturedBy => Team;
    }

    private class SampleObjective : IMatchObjective
    {
        public Team? Capture;
        public int AdvanceCalls { get; private set; }
        public List<(int Tick, bool BlueAlive, bool RedAlive)> Samples { get; } = [];
        public ObjectiveState State { get; private set; } = new CaptureState(null);
        public void Advance(SimWorld world)
        {
            AdvanceCalls++;
            if (State.CapturedBy != null)
            {
                return;
            }
            Samples.Add((world.Tick, world.Players.TryGetValue(1, out PlayerState blue) && blue.IsAlive,
                world.Players.TryGetValue(2, out PlayerState red) && red.IsAlive));
            State = new CaptureState(Capture);
        }
    }
}
