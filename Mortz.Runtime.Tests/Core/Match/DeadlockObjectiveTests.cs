using System.Globalization;
using Mortz.Core.Match.Objectives;
using Mortz.Core.Match.Teams;
using Xunit;

namespace Mortz.Runtime.Tests.Core.Match;

public class DeadlockObjectiveTests
{
    [Fact]
    public void GraceCountdownMatchesTheFirstDecayTickAndResetsOnReturn()
    {
        DeadlockObjective objective = new(100, 10, 10, 2);
        objective.Advance(true, false);
        objective.Advance(false, false);
        Assert.Equal(2, objective.State.BlueGraceTicks);
        Assert.False(objective.State.BlueDecaying);
        objective.Advance(false, false);
        Assert.Equal(1, objective.State.BlueGraceTicks);
        objective.Advance(false, false);
        Assert.Equal(0, objective.State.BlueGraceTicks);
        Assert.True(objective.State.BlueDecaying);
        objective.Advance(true, false);
        Assert.Equal(0, objective.State.BlueGraceTicks);
        Assert.False(objective.State.BlueDecaying);
        objective.Advance(false, false);
        Assert.Equal(2, objective.State.BlueGraceTicks);
    }

    [Fact]
    public void FractionalFrontsReachFullCaptureWithoutAnEpsilon()
    {
        DeadlockObjective objective = new(101, 30, 10, 0);
        objective.Advance(true, false);
        objective.Advance(true, true);
        objective.Advance(true, true);
        Assert.Equal(65.5m, objective.State.BlueProgress);
        Advance(objective, 3, true, false);
        Assert.Equal(95.5m, objective.State.BlueProgress);
        Assert.Equal(5.5m, objective.State.RedProgress);
        Assert.Null(objective.State.CapturedBy);
        objective.Advance(true, false);
        Assert.Equal(101, objective.State.BlueProgress);
        Assert.Equal(0, objective.State.RedProgress);
        Assert.Equal(Team.BLUE, objective.State.CapturedBy);
    }

    [Theory]
    [InlineData(0, "---", "30,20,10")]
    [InlineData(2, "----", "40,40,30,20")]
    [InlineData(2, "--B---", "40,40,50,50,50,40")]
    [InlineData(2, "---B---", "40,40,30,40,40,40,30")]
    [InlineData(2, "-B-B---", "40,50,50,60,60,60,50")]
    public void AbsenceAndReturnBoundaries(int grace, string occupancy, string expected)
    {
        DeadlockObjective objective = new(100, 10, 10, grace);
        Advance(objective, 4, true, false);
        int[] progress = expected.Split(',').Select(int.Parse).ToArray();
        for (int i = 0; i < occupancy.Length; i++)
        {
            objective.Advance(occupancy[i] == 'B', false);
            Assert.Equal(progress[i], objective.State.BlueProgress);
        }
    }

    [Fact]
    public void EmptyStartAndCaptureCompletionAreStable()
    {
        DeadlockObjective objective = new(100, 30, 30, 2);
        Advance(objective, 10, false, false);
        Assert.Equal(100, objective.State.NeutralProgress);
        Advance(objective, 3, true, false);
        Assert.Null(objective.State.CapturedBy);
        Assert.Equal(90, objective.State.BlueProgress);
        objective.Advance(true, false);
        Assert.Equal(Team.BLUE, objective.State.CapturedBy);
        Assert.Equal(100, objective.State.BlueProgress);
        DeadlockObjectiveState completed = objective.State;
        Advance(objective, 100, false, true);
        Assert.Same(completed, objective.State);
    }

    [Theory]
    [InlineData(100, 30, 0, "50", "50")]
    [InlineData(100, 30, 1, "65", "35")]
    [InlineData(101, 30, 1, "65.5", "35.5")]
    public void ContactSplitsTheRemainingGapAtTheSameInstant(int capacity, int speed, int blueHeadStart,
        string blue, string red)
    {
        DeadlockObjective objective = new(capacity, speed, speed, 2);
        Advance(objective, blueHeadStart, true, false);
        Advance(objective, 5, true, true);
        Assert.Equal(decimal.Parse(blue, CultureInfo.InvariantCulture), objective.State.BlueProgress);
        Assert.Equal(decimal.Parse(red, CultureInfo.InvariantCulture), objective.State.RedProgress);
        Assert.True(objective.State.IsDeadlocked);
        Assert.Null(objective.State.CapturedBy);
    }

    [Theory]
    [InlineData(5, 55, 45)]
    [InlineData(10, 60, 40)]
    [InlineData(20, 60, 30)]
    public void CaptureClaimsReleasedSpaceOnTheSameTick(int decay, int blue, int red)
    {
        DeadlockObjective objective = new(100, 10, decay, 0);
        Advance(objective, 5, true, true);
        objective.Advance(true, false);
        Assert.Equal(blue, objective.State.BlueProgress);
        Assert.Equal(red, objective.State.RedProgress);
    }

    [Fact]
    public void TeamsDecayIndependentlyAndReturningTeamsShareTheGap()
    {
        DeadlockObjective objective = new(100, 10, 10, 2);
        Advance(objective, 5, true, true);
        objective.Advance(false, true);
        objective.Advance(false, false);
        objective.Advance(false, false);
        Assert.Equal(40, objective.State.BlueProgress);
        Assert.Equal(50, objective.State.RedProgress);
        Assert.True(objective.State.BlueDecaying);
        Assert.False(objective.State.RedDecaying);
        objective.Advance(false, false);
        Assert.Equal(30, objective.State.BlueProgress);
        Assert.Equal(40, objective.State.RedProgress);
        Advance(objective, 2, true, true);
        Assert.Equal(45, objective.State.BlueProgress);
        Assert.Equal(55, objective.State.RedProgress);
        Assert.True(objective.State.IsDeadlocked);
    }

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(17, 7, 2)]
    [InlineData(7, 31, 0)]
    public void SwappingColorsMirrorsEveryTransitionWithoutOverlap(int capture, int decay, int grace)
    {
        Random random = new(934);
        for (int trial = 0; trial < 100; trial++)
        {
            DeadlockObjective original = new(101, capture, decay, grace);
            DeadlockObjective mirrored = new(101, capture, decay, grace);
            for (int tick = 0; tick < 1000; tick++)
            {
                bool blue = random.Next(2) == 0;
                bool red = random.Next(2) == 0;
                original.Advance(blue, red);
                mirrored.Advance(red, blue);
                Assert.Equal(original.State.BlueProgress, mirrored.State.RedProgress);
                Assert.Equal(original.State.RedProgress, mirrored.State.BlueProgress);
                Assert.InRange(original.State.BlueProgress, 0, 101);
                Assert.InRange(original.State.RedProgress, 0, 101);
                Assert.InRange(original.State.NeutralProgress, 0, 101);
                Assert.Equal(original.State.CapturedBy.HasValue, mirrored.State.CapturedBy.HasValue);
                if (original.State.CapturedBy.HasValue)
                {
                    Assert.NotEqual(original.State.CapturedBy, mirrored.State.CapturedBy);
                }
            }
        }
    }

    private static void Advance(DeadlockObjective objective, int ticks, bool blue, bool red)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            objective.Advance(blue, red);
        }
    }
}
