using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Respawning;
using Mortz.Core.Sim;
using Xunit;

namespace Mortz.Runtime.Tests.Core.Match;

public class EscalatingRespawnTests
{
    [Theory]
    [InlineData(2, 1, 4, 1UL, 180)]
    [InlineData(2, 1, 4, ulong.MaxValue, 240)]
    [InlineData(0, 1, 4, 0UL, 1)]
    [InlineData(0.35f, 0, 1, 0UL, 21)]
    [InlineData(1, 1, 1092.25f, ulong.MaxValue, ushort.MaxValue)]
    public void DelayIsCappedBeforeConversion(float baseDelay, float increase, float maximum,
        ulong previousDeaths, int expected)
    {
        EscalatingRespawnStrategy strategy = new(new EscalatingRespawnRules
        {
            BaseDelay = baseDelay,
            Increase = increase,
            MaximumDelay = maximum,
        }.ToSnapshot());
        Assert.Equal(expected, strategy.DelayTicks(previousDeaths));
    }

    [Fact]
    public void DeathsSurviveRespawnsButNotRemovalOrANewMatch()
    {
        EscalatingRespawnStrategy strategy = new(new EscalatingRespawnRules
        {
            BaseDelay = 1,
            Increase = 1,
            MaximumDelay = 3,
        }.ToSnapshot());
        strategy.Spawned(1, true);
        Assert.Null(strategy.ReturnTick(1, 0));
        for (int death = 0; death < 5; death++)
        {
            int tick = death * 1000;
            strategy.Died(new Death(1, default, death % 2 == 0 ? 1 : 2, false, -1), tick);
            int expected = tick + Math.Min(death + 1, 3) * SimConfig.TICK_RATE;
            Assert.Equal(expected, strategy.ReturnTick(1, tick));
            Assert.Equal(expected, strategy.ReturnTick(1, expected + 1));
            strategy.Spawned(1, false);
            Assert.Null(strategy.ReturnTick(1, expected));
        }
        strategy.Died(new Death(2, default, -1, false, -1), 0);
        Assert.Equal(60, strategy.ReturnTick(2, 0));
        strategy.Removed(1);
        strategy.Spawned(1, true);
        strategy.Died(new Death(1, default, -1, false, -1), 0);
        Assert.Equal(60, strategy.ReturnTick(1, 0));
        EscalatingRespawnStrategy fresh = new(new EscalatingRespawnRules { BaseDelay = 1 }.ToSnapshot());
        fresh.Died(new Death(1, default, -1, false, -1), 0);
        Assert.Equal(60, fresh.ReturnTick(1, 0));
    }

    [Fact]
    public void SimulationCountsEachDeathOnceAndRespawnsOnTheCappedSchedule()
    {
        SimWorld world = new(TestWorlds.Flat(), new MatchConfig
        {
            Rules = new ModeRules
            {
                SpawnImmunity = 0,
                Respawn = new EscalatingRespawnRules { BaseDelay = 0, Increase = 0.25f, MaximumDelay = 0.5f },
            },
        });
        world.AddPlayer(1);
        foreach (int delay in new[] { 1, 15, 30, 30 })
        {
            world.QueueDamage(1, byte.MaxValue);
            world.QueueDamage(1, byte.MaxValue);
            world.Step();
            Assert.Single(world.Deaths);
            Assert.Equal(delay, world.Players[1].RespawnTicks);
            for (int remaining = delay - 1; remaining > 0; remaining--)
            {
                world.QueueDamage(1, byte.MaxValue);
                world.Step();
                Assert.False(world.Players[1].IsAlive);
                Assert.Equal(remaining, world.Players[1].RespawnTicks);
            }
            world.Step();
            Assert.True(world.Players[1].IsAlive);
        }
    }
}
