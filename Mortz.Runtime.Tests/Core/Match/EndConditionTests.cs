using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Scoring;
using Mortz.Core.Sim;
using Xunit;

namespace Mortz.Runtime.Tests.Core.Match;

public class EndConditionTests
{
    private static readonly Victor _first = new Victor.Player(1);
    private static readonly Victor _third = new Victor.Player(3);

    [Fact]
    public void ScoreTargetWinsAtTheTargetButATieWaits()
    {
        EndConditionRulesSnapshot target = new ScoreTargetRulesSnapshot(2);
        Assert.Null(target.Winner(0, Kills(1, 0)));
        Assert.Equal(_first, target.Winner(0, Kills(2, 0)));
        Assert.Null(target.Winner(0, Kills(2, 2)));
    }

    [Fact]
    public void ScoreTargetMatchPointIsOneKillAway()
    {
        Assert.Equal(new MatchPoint(1, _first), new ScoreTargetRulesSnapshot(2).MatchPoint(Kills(1, 0)));
        Assert.Null(new ScoreTargetRulesSnapshot(2).MatchPoint(Kills(0, 0)));
        Assert.Equal(new MatchPoint(1, null), new ScoreTargetRulesSnapshot(1).MatchPoint(Kills(0, 0)));
    }

    [Fact]
    public void ScoreLeadCountsTheLeadOverTheRunnerUp()
    {
        EndConditionRulesSnapshot lead = new ScoreLeadRulesSnapshot(2);
        Assert.Equal(new MatchPoint(1, _first), lead.MatchPoint(Kills(1, 0, 0)));
        Assert.Null(lead.MatchPoint(Kills(1, 1, 0)));
        Assert.Null(lead.Winner(0, Kills(2, 1, 0)));
        Assert.Equal(_first, lead.Winner(0, Kills(3, 1, 0)));
        Assert.Equal(_first, lead.Winner(0, Kills(1, -1)));
    }

    [Fact]
    public void ScoreLeadNeedsTwoContenders()
    {
        EndConditionRulesSnapshot lead = new ScoreLeadRulesSnapshot(1);
        Assert.Null(lead.Winner(0, Kills(5)));
        Assert.Equal(new MatchPoint(1, null), lead.MatchPoint(Kills(5)));
    }

    [Fact]
    public void DeathsWinLowAndNeverAnnounceAMatchPoint()
    {
        Assert.Equal(_third, new ScoreTargetRulesSnapshot(2).Winner(0, Deaths(2, 1, 0)));
        Assert.Equal(_third, new ScoreLeadRulesSnapshot(2).Winner(0, Deaths(2, 2, 0)));
        Assert.Null(new ScoreTargetRulesSnapshot(2).MatchPoint(Deaths(1, 0, 0)));
        Assert.Null(new ScoreLeadRulesSnapshot(2).MatchPoint(Deaths(1, 0, 0)));
    }

    [Fact]
    public void TimeLimitPicksTheBestOnceTimeIsUpButATieWaits()
    {
        EndConditionRulesSnapshot time = new TimeLimitRulesSnapshot(1);
        Assert.Null(time.Winner(SimConfig.TICK_RATE - 1, Kills(1, 0)));
        Assert.Equal(_first, time.Winner(SimConfig.TICK_RATE, Kills(1, 0)));
        Assert.Null(time.Winner(SimConfig.TICK_RATE, Kills(1, 1)));
    }

    private static Scores Kills(params int[] values) => Of(values, higherIsBetter: true);

    private static Scores Deaths(params int[] values) => Of(values, higherIsBetter: false);

    private static Scores Of(int[] values, bool higherIsBetter) => new(
        values.Select((value, index) => new ContenderScore(new Victor.Player(index + 1), value)).ToArray(),
        higherIsBetter);
}
