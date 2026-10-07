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
    public void ACaptureBeatsASameTickDecisiveKillAndTheKillStillScores()
    {
        SampleObjective objective = new() { Capture = Team.BLUE };
        ModeFixture fixture = new(new ModeRules
        {
            Teams = true,
            EndCondition = new ScoreTargetRules { Target = 1 },
        }, objective);
        fixture.Seat(1, Team.BLUE);
        fixture.Seat(2, Team.RED);

        GameModeUpdate update = fixture.Advance(fixture.Death(victimId: 1, killerId: 2));

        Assert.Equal(new Victor.Team(Team.BLUE), update.Outcome!.Winner);
        Assert.Null(update.Outcome.FinalKill);
        Assert.Single(update.Eliminations);
        Assert.Equal(1, fixture.Scoring.TeamKills.Red);
        Assert.Null(update.MatchPoint);
    }

    [Fact]
    public void TheObjectiveAdvancesOncePerTickWhateverTheDeaths()
    {
        SampleObjective objective = new();
        ModeFixture fixture = new(new ModeRules { EndCondition = new NoEndConditionRules() }, objective);
        fixture.Seat(1);
        fixture.Seat(2);
        fixture.World.Step();

        GameModeUpdate update = fixture.Advance(
            fixture.Death(victimId: 1, killerId: 2),
            fixture.Death(victimId: 2, killerId: 1));

        Assert.Equal(2, update.Eliminations.Count);
        Assert.Null(update.Outcome);
        Assert.Equal([fixture.World.Tick], objective.Ticks);
    }

    [Fact]
    public void NoObjectiveHasNoWinnerAndNeverChanges()
    {
        using MatchRuntime runtime = new(new TerrainMask(8, 8, (_, _) => false, (_, _) => false),
            new MatchConfig(), 0, new MatchStateKeys(1));
        runtime.Advance(default);
        runtime.Advance(default);
        Assert.Same(NoObjectiveState.Instance, runtime.Objective);
        Assert.Null(NoObjective.Instance.Winner);
    }

    private record SampleState : ObjectiveState;

    private class SampleObjective : MatchObjective
    {
        public Team? Capture;
        public List<int> Ticks { get; } = [];
        public override ObjectiveState State { get; } = new SampleState();
        public override Victor? Winner => Capture is { } team ? new Victor.Team(team) : null;
        public override void Advance(SimWorld world) => Ticks.Add(world.Tick);
    }
}
