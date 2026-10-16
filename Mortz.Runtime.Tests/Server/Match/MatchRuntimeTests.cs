using Mortz.Core.Features;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Participation;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Server.Match;
using Mortz.Server.Players;
using Xunit;

namespace Mortz.Runtime.Tests.Server.Match;

public class MatchRuntimeTests
{
    private const int GENERATION = 4;

    [Fact]
    public void AnOutcomeWithoutADeathEndsTheMatchWithoutInventingAReplay()
    {
        FeatureScope scope = new("match");
        MatchRuntime runtime = new(new TerrainMask(128, 128, (_, _) => false, (_, _) => false),
            new MatchConfig
            {
                Rules = new ModeRules
                {
                    EndCondition = new TimeLimitRules { Seconds = 1 },
                }
            }, 2, scope, GENERATION);
        runtime.Seat(OpenPlayer(1));
        Assert.Null(runtime.Advance(default).MatchEnded);
        while (runtime.World.Tick < SimConfig.TICK_RATE - 1)
        {
            Assert.Null(runtime.Advance(default).MatchEnded);
        }
        MatchUpdate ended = runtime.Advance(default);
        Assert.Equal(new Victor.Player(1), ended.MatchEnded);
        Assert.Empty(ended.Deaths);
        Assert.Null(ended.FinalKill);
        Assert.Equal(MatchStage.VICTORY_LAP, runtime.Stage);
        runtime.Advance(default);
        Assert.True(runtime.Advance(default).ReturnToLobby);
        Assert.Equal(SimConfig.TICK_RATE, runtime.World.Tick);
    }

    [Fact]
    public void MutatingEditableRulesCannotChangeAnActiveMatch()
    {
        MatchConfig editable = new();
        FeatureScope scope = new("match");
        MatchRuntime runtime = new(new TerrainMask(128, 128, (_, _) => false, (_, _) => false),
            editable, 2, scope, GENERATION);
        runtime.Seat(OpenPlayer(1));
        MatchConfigSnapshot frozen = runtime.Config;
        PlayerStats stats = runtime.World.Stats[1];
        editable.Physics.Gravity = 0;
        ((ScoreTargetRules)editable.Rules.EndCondition).Target = 1;
        editable.Combat.MortarCarveRadius = 1;
        Assert.Equal(frozen, runtime.Config);
        Assert.Equal(stats, runtime.World.Stats[1]);
        runtime.Config.ToMutable().Physics.Gravity = 1;
        Assert.Equal(frozen, runtime.Config);
    }

    [Fact]
    public void AdvanceRunsTheFullDependencyOrderedPipeline()
    {
        FeatureScope scope = new("match");
        MatchRuntime runtime = NewRuntime(scope);
        Player victim = OpenPlayer(1);
        runtime.Seat(victim);
        runtime.World.QueueDamage(victim.PeerId, byte.MaxValue);

        MatchUpdate update = runtime.Advance(default);

        Assert.Equal(1, update.Tick);
        Assert.Single(update.Deaths);
        Assert.Single(update.Eliminations);
        Assert.Single(update.ParticipationChanges);
        Assert.NotEmpty(update.GameEvents);
    }

    [Fact]
    public void AuthoredSpawnPointsReachTheWorld()
    {
        FeatureScope scope = new("match");
        MatchRuntime runtime = NewRuntime(scope,
            spawnPoints: [new Vec2(32, 64), new Vec2(96, 64)]);

        runtime.Seat(OpenPlayer(50));
        runtime.Seat(OpenPlayer(10));

        Assert.Equal(new Vec2(32, 64), runtime.World.Players[50].Position);
        Assert.Equal(new Vec2(96, 64), runtime.World.Players[10].Position);
    }

    [Fact]
    public void LobbyTeamsCarryIntoTheMatchAndLateJoinersBalance()
    {
        FeatureScope scope = new("match");
        MatchRuntime runtime = NewRuntime(scope, teams: true);

        runtime.Seat(OpenPlayer(1), Team.RED);
        runtime.Seat(OpenPlayer(2), Team.RED);
        runtime.Seat(OpenPlayer(3), Team.BLUE);

        Team? lateJoiner = runtime.Seat(OpenPlayer(4));

        Assert.Equal(Team.RED, runtime.World.Players[1].Team);
        Assert.Equal(Team.RED, runtime.World.Players[2].Team);
        Assert.Equal(Team.BLUE, runtime.World.Players[3].Team);
        Assert.Equal(Team.BLUE, lateJoiner);
    }

    [Fact]
    public void LobbyTeamsAreIgnoredWhenTeamsAreOff()
    {
        FeatureScope scope = new("match");
        MatchRuntime runtime = NewRuntime(scope);

        Assert.Null(runtime.Seat(OpenPlayer(1), Team.RED));
    }

    [Fact]
    public void SeatingSpectatingAndRemovalAreOwnedByTheRuntime()
    {
        FeatureScope scope = new("match");
        MatchRuntime runtime = NewRuntime(scope);
        Player seated = OpenPlayer(1);
        Player spectator = OpenPlayer(2);

        runtime.Seat(seated);
        runtime.AddJipSpectator(spectator);

        Assert.Contains(seated.PeerId, runtime.World.Players.Keys);
        Assert.DoesNotContain(spectator.PeerId, runtime.World.Players.Keys);
        Assert.Equal(MatchParticipation.JipSpectator, runtime.ParticipationOf(spectator));
        Assert.Equal([seated.PeerId],
            runtime.ScoreRows().Select(row => row.Player.PeerId));

        runtime.Remove(seated);

        Assert.DoesNotContain(seated.PeerId, runtime.World.Players.Keys);
        Assert.Empty(runtime.ScoreRows());
    }

    [Fact]
    public void WinnersIncludeOnlyPlayersStillSeated()
    {
        FeatureScope scope = new("match");
        MatchRuntime runtime = NewRuntime(scope, teams: true);
        Player blue = OpenPlayer(1);
        Player red = OpenPlayer(2);
        Player departedBlue = OpenPlayer(3);
        runtime.Seat(blue, Team.BLUE);
        runtime.Seat(red, Team.RED);
        runtime.Seat(departedBlue, Team.BLUE);
        runtime.Remove(departedBlue);

        Assert.Equal([1],
            runtime.Winners(new Victor.Team(Team.BLUE)).Select(player => player.PeerId));
        Assert.Equal([2],
            runtime.Winners(new Victor.Player(2)).Select(player => player.PeerId));
    }

    [Fact]
    public void VictoryLapFreezesTheWorldRejectsInputsAndUsesItsOwnCountdown()
    {
        FeatureScope scope = new("match");
        MatchRuntime runtime = NewRuntime(scope,
            killTarget: 1,
            victoryLapTicks: 3,
            suicidePenalty: SuicidePenalty.REWARD_CLOSEST_ENEMY,
            spawnPoints: [new Vec2(10, 32), new Vec2(40, 32)]);
        Player winner = OpenPlayer(1);
        Player victim = OpenPlayer(2);
        runtime.Seat(winner);
        runtime.Seat(victim);
        runtime.World.QueueDamage(victim.PeerId, byte.MaxValue);

        MatchUpdate winningUpdate = runtime.Advance(default);

        Assert.Equal(new Victor.Player(winner.PeerId), winningUpdate.MatchEnded);
        Assert.Equal(MatchStage.VICTORY_LAP, runtime.Stage);
        int frozenTick = runtime.World.Tick;
        runtime.EnqueueInput(winner, 0, new PlayerInput(InputButtons.RIGHT));
        Assert.Equal(0, runtime.World.PendingInputs(winner.PeerId));

        MatchUpdate first = runtime.Advance(default);
        MatchUpdate second = runtime.Advance(default);
        MatchUpdate third = runtime.Advance(default);

        Assert.Equal(frozenTick, runtime.World.Tick);
        Assert.False(first.ReturnToLobby);
        Assert.False(second.ReturnToLobby);
        Assert.True(third.ReturnToLobby);
        Assert.Empty(first.MortarEvents);
        Assert.Empty(first.Explosions);
    }

    private static MatchRuntime NewRuntime(
        FeatureScope scope,
        bool teams = false,
        int killTarget = 20,
        int victoryLapTicks = 3,
        SuicidePenalty suicidePenalty = SuicidePenalty.NONE,
        IReadOnlyList<Vec2>? spawnPoints = null)
    {
        TerrainMask terrain = new(128, 128, (_, _) => false, (_, _) => false);
        return new MatchRuntime(terrain, new MatchConfig
        {
            Rules = new ModeRules
            {
                Teams = teams,
                EndCondition = new ScoreTargetRules { Target = killTarget },
                SuicidePenalty = suicidePenalty,
                SpawnImmunity = 0,
            },
        }, victoryLapTicks, scope, GENERATION,
            spawnPoints?.Select(point => new SpawnPoint(point)).ToArray());
    }

    private static Player OpenPlayer(int peerId) => new(peerId, $"Player {peerId}");
}
