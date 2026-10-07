using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;
using Mortz.Server.Match.Modes;
using Mortz.Server.Match.Scoring;
using Xunit;

namespace Mortz.Runtime.Tests.Server.Match;

public class ScoringStepTests
{
    [Fact]
    public void ProducesOrderedEliminationsAndTheMatchPoint()
    {
        ModeFixture fixture = new(Rules(target: 3));
        fixture.Seat(1);
        fixture.Seat(2);
        fixture.Seat(3);

        GameModeUpdate tick = fixture.Advance(
            fixture.Death(victimId: 2, killerId: 1),
            fixture.Death(victimId: 3, killerId: 1));

        Assert.Equal([2, 3], tick.Eliminations.Select(kill => kill.Victim.PeerId));
        Assert.True(tick.Eliminations[0].FirstBlood);
        Assert.False(tick.Eliminations[1].FirstBlood);
        Assert.Equal(new MatchPoint(1, new Victor.Player(1)), tick.MatchPoint);
        Assert.Null(tick.Outcome);
    }

    [Fact]
    public void TheFirstDecisiveDeathWinsAndLaterDeathsStillScore()
    {
        ModeFixture fixture = new(Rules(target: 1));
        fixture.Seat(1);
        fixture.Seat(2);
        fixture.Seat(3);
        Death winningDeath = fixture.Death(victimId: 2, killerId: 1);

        GameModeUpdate tick = fixture.Advance(winningDeath, fixture.Death(victimId: 3, killerId: 2));

        Assert.Equal(2, tick.Eliminations.Count);
        Assert.Equal(new Victor.Player(1), tick.Outcome!.Winner);
        Assert.Equal(winningDeath, tick.Outcome.FinalKill!.Value.Death);
        Assert.Equal(tick.Eliminations[0], tick.Outcome.FinalKill.Value.Kill);
        Assert.Equal(1, fixture.Scoring.ScoreOf(fixture.Player(3)).Deaths);
        Assert.Equal(1, fixture.Scoring.ScoreOf(fixture.Player(2)).Kills);
        Assert.Null(tick.MatchPoint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinalReplayOnlyChangesTheReplay(bool finalReplay)
    {
        ModeRules rules = Rules(target: 1);
        rules.FinalReplay = finalReplay;
        ModeFixture fixture = new(rules);
        fixture.Seat(1);
        fixture.Seat(2);

        GameModeUpdate update = fixture.Advance(fixture.Death(victimId: 2, killerId: 1));

        Assert.Equal(new Victor.Player(1), update.Outcome!.Winner);
        Assert.Equal(finalReplay, update.Outcome.FinalKill != null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ATiedTimeLimitPlaysOnAndTheKillThatBreaksItIsReplayed(bool finalReplay)
    {
        ModeFixture fixture = new(new ModeRules
        {
            EndCondition = new TimeLimitRules { Seconds = 1 },
            FinalReplay = finalReplay,
        });
        fixture.Seat(1);
        fixture.Seat(2);
        while (fixture.World.Tick < SimConfig.TICK_RATE)
            fixture.World.Step();
        Assert.Null(fixture.Advance().Outcome);

        GameModeUpdate update = fixture.Advance(fixture.Death(victimId: 2, killerId: 1));

        Assert.Equal(new Victor.Player(1), update.Outcome!.Winner);
        Assert.Equal(finalReplay, update.Outcome.FinalKill != null);
    }

    [Fact]
    public void UncreditedDeathDoesNotConsumeFirstBlood()
    {
        ModeFixture fixture = new(Rules());
        fixture.Seat(1);
        fixture.Seat(2);

        GameModeUpdate tick = fixture.Advance(
            fixture.Death(victimId: 2, killerId: 99),
            fixture.Death(victimId: 2, killerId: 1));

        Assert.False(tick.Eliminations[0].FirstBlood);
        Assert.True(tick.Eliminations[1].FirstBlood);
    }

    [Fact]
    public void TeamKillDoesNotConsumeFirstBlood()
    {
        ModeFixture fixture = new(Rules(teams: true));
        fixture.Seat(1, Team.BLUE);
        fixture.Seat(2, Team.RED);
        fixture.Seat(3, Team.BLUE);

        GameModeUpdate tick = fixture.Advance(
            fixture.Death(victimId: 3, killerId: 1),
            fixture.Death(victimId: 2, killerId: 1));

        Assert.Equal(DeathKind.TEAM_KILL, tick.Eliminations[0].Score.Kind);
        Assert.False(tick.Eliminations[0].FirstBlood);
        Assert.True(tick.Eliminations[1].FirstBlood);
    }

    [Fact]
    public void RewardedSuicideGoesToTheNearestLivingEnemy()
    {
        ModeFixture fixture = new(Rules(suicidePenalty: SuicidePenalty.REWARD_CLOSEST_ENEMY));
        fixture.Seat(1);
        fixture.Seat(2);
        fixture.Seat(3);
        fixture.World.Teleport(1, new Vec2(10, 32));
        fixture.World.Teleport(2, new Vec2(40, 32));
        fixture.World.Teleport(3, new Vec2(120, 32));

        GameModeUpdate tick = fixture.Advance(fixture.Death(victimId: 1, killerId: 1));

        Assert.Equal(new KillReward(2, 1), Assert.Single(tick.Eliminations).Score.Reward);
        Assert.Equal(1, fixture.Scoring.ScoreOf(fixture.Player(2)).Kills);
    }

    [Fact]
    public void RewardedSuicideNeverPaysATeammate()
    {
        ModeFixture fixture = new(Rules(teams: true, suicidePenalty: SuicidePenalty.REWARD_CLOSEST_ENEMY));
        fixture.Seat(1, Team.BLUE);
        fixture.Seat(2, Team.BLUE);
        fixture.Seat(3, Team.RED);
        fixture.World.Teleport(1, new Vec2(10, 32));
        fixture.World.Teleport(2, new Vec2(40, 32));
        fixture.World.Teleport(3, new Vec2(120, 32));

        GameModeUpdate tick = fixture.Advance(fixture.Death(victimId: 1, killerId: 0));

        Assert.Equal(new KillReward(3, 1), Assert.Single(tick.Eliminations).Score.Reward);
        Assert.Equal(0, fixture.Scoring.ScoreOf(fixture.Player(2)).Kills);
    }

    private static ModeRules Rules(int target = 20, bool teams = false,
        SuicidePenalty suicidePenalty = SuicidePenalty.NONE) => new()
        {
            Teams = teams,
            EndCondition = new ScoreTargetRules { Target = target },
            SuicidePenalty = suicidePenalty,
        };
}
