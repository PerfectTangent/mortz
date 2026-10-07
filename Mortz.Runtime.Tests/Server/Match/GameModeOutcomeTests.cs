using Mortz.Core.Features;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Server.Match;
using Mortz.Server.Players;
using Xunit;

namespace Mortz.Runtime.Tests.Server.Match;

public class GameModeOutcomeTests
{
    [Fact]
    public void TheTimerDecidesOnTheScoresBeforeTheTickAndEveryDeathOnThatTickStillCounts()
    {
        using Fixture fixture = new(TimedDeathRules());
        fixture.Runtime.World.QueueDamage(2, byte.MaxValue);
        fixture.Runtime.World.QueueDamage(3, byte.MaxValue);
        Assert.Null(fixture.Runtime.Advance(default).MatchEnded);
        fixture.Runtime.World.QueueDamage(2, byte.MaxValue);
        Assert.Null(fixture.Runtime.Advance(default).MatchEnded);

        fixture.AdvanceToDeadline();
        fixture.Runtime.World.QueueDamage(1, byte.MaxValue);
        fixture.Runtime.World.QueueDamage(3, byte.MaxValue);
        MatchUpdate update = fixture.Runtime.Advance(default);

        Assert.Equal(new Victor.Player(1), update.MatchEnded);
        Assert.Equal(2, update.Deaths.Length);
        Assert.Equal(2, update.Eliminations.Length);
        Assert.Equal(1, fixture.Runtime.ScoreOf(fixture.Players[0]).Deaths);
        Assert.Equal(2, fixture.Runtime.ScoreOf(fixture.Players[1]).Deaths);
        Assert.Equal(2, fixture.Runtime.ScoreOf(fixture.Players[2]).Deaths);
        Assert.Null(update.FinalKill);
        Assert.Null(fixture.Runtime.FinalKill);
        Assert.Equal(MatchStage.VICTORY_LAP, fixture.Runtime.Stage);
    }

    [Fact]
    public void TheFewestDeathsWinAtTheTimeLimit()
    {
        using Fixture fixture = new(TimedDeathRules());
        fixture.Runtime.World.QueueDamage(1, byte.MaxValue);
        fixture.Runtime.World.QueueDamage(2, byte.MaxValue);
        fixture.AdvanceToDeadline();

        MatchUpdate ended = fixture.Runtime.Advance(default);

        Assert.Equal(new Victor.Player(3), ended.MatchEnded);
        Assert.Null(ended.FinalKill);
    }

    [Theory]
    [InlineData("score_target", 1)]
    [InlineData("score_lead", 2)]
    public void ReachingADeathTargetEndsTheMatchForTheFewestDeathsWithoutAMatchPoint(
        string condition, int decidingVictim)
    {
        using Fixture fixture = new(new ModeRules
        {
            Score = ScoreMetric.DEATHS,
            EndCondition = condition == "score_lead" ? new ScoreLeadRules { Target = 2 } : new ScoreTargetRules { Target = 2 },
        });
        fixture.Runtime.World.QueueDamage(1, byte.MaxValue);
        fixture.Runtime.World.QueueDamage(2, byte.MaxValue);
        MatchUpdate first = fixture.Runtime.Advance(default);
        Assert.Null(first.MatchEnded);
        Assert.Null(first.MatchPoint);
        fixture.Runtime.World.QueueDamage(1, byte.MaxValue);
        fixture.Runtime.World.QueueDamage(2, byte.MaxValue);

        MatchUpdate ended = fixture.Runtime.Advance(default);

        Assert.Equal(new Victor.Player(3), ended.MatchEnded);
        Assert.Equal(2, ended.Eliminations.Length);
        Assert.Equal(decidingVictim, ended.FinalKill!.Value.Death.PeerId);
        Assert.Null(ended.MatchPoint);
    }

    [Fact]
    public void TeamDeathTotalsSurviveAPlayerLeaving()
    {
        ModeRules rules = TimedDeathRules();
        rules.Teams = true;
        using Fixture fixture = new(rules);
        Team losingTeam = fixture.Runtime.World.Players[1].Team!.Value;
        Team winningTeam = Teams.All.Single(team => team != losingTeam);
        fixture.Runtime.World.QueueDamage(1, byte.MaxValue);
        fixture.Runtime.Advance(default);
        fixture.Runtime.Remove(fixture.Players[0]);
        fixture.AdvanceToDeadline();

        Assert.Equal(new Victor.Team(winningTeam), fixture.Runtime.Advance(default).MatchEnded);
    }

    [Fact]
    public void AnEmptyTeamCannotWinWithZeroDeaths()
    {
        ModeRules rules = TimedDeathRules();
        rules.Teams = true;
        using Fixture fixture = new(rules);
        Team remainingTeam = fixture.Runtime.World.Players[1].Team!.Value;
        fixture.Runtime.Remove(fixture.Players[1]);
        fixture.Runtime.World.QueueDamage(1, byte.MaxValue);
        fixture.AdvanceToDeadline();

        Assert.Equal(new Victor.Team(remainingTeam), fixture.Runtime.Advance(default).MatchEnded);
    }

    private static ModeRules TimedDeathRules() => new()
    {
        Score = ScoreMetric.DEATHS,
        EndCondition = new TimeLimitRules { Seconds = 1 },
    };

    private class Fixture : IDisposable
    {
        public MatchRuntime Runtime { get; }
        public Player[] Players { get; }

        public Fixture(ModeRules rules)
        {
            rules.SpawnImmunity = 0;
            rules.Respawn = new FixedRespawnRules { Delay = 0 };
            FeatureScope scope = new("match");
            Runtime = new MatchRuntime(new TerrainMask(128, 128, (_, y) => y >= 96, (_, _) => false),
                new MatchConfig { Rules = rules },
                2, scope, 1, [new SpawnPoint(new Vec2(8, 96)), new SpawnPoint(new Vec2(24, 96)),
                    new SpawnPoint(new Vec2(40, 96))]);
            Players = Enumerable.Range(1, 3).Select(id => new Player(id, $"Player {id}")).ToArray();
            foreach (Player player in Players)
            {
                Runtime.Seat(player);
            }
        }

        public void AdvanceToDeadline()
        {
            while (Runtime.World.Tick < SimConfig.TICK_RATE - 1)
            {
                Assert.Null(Runtime.Advance(default).MatchEnded);
            }
        }

        public void Dispose()
        {
            Runtime.Dispose();
        }
    }
}
