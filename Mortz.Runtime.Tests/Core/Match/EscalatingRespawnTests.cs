using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Respawning;
using Mortz.Core.Match.Teams;
using Mortz.Core.Sim;
using Xunit;

namespace Mortz.Runtime.Tests.Core.Match;

public class EscalatingRespawnTests
{
    [Theory]
    [InlineData("fall")]
    [InlineData("damage")]
    [InlineData("suicide")]
    [InlineData("friendly fire")]
    public void EveryDeathCauseIncreasesTheNextDelay(string cause)
    {
        SimWorld world = new(TestWorlds.Flat(), new MatchConfig
        {
            Rules = new ModeRules
            {
                Teams = true,
                FriendlyFire = true,
                SpawnImmunity = 0,
                Respawn = new EscalatingRespawnRules { BaseDelay = 1, Increase = 1, MaximumDelay = 3 },
            },
        });
        world.AddPlayer(1, Team.BLUE);
        int victim = 1;
        if (cause == "friendly fire")
        {
            world.AddPlayer(2, Team.BLUE);
            world.Teleport(2, world.Players[1].Position);
            victim = 2;
        }
        if (cause == "fall")
        {
            world.Teleport(1, new Vec2(200, world.Terrain.Height + SimConfig.DEATH_PIT_DEPTH + 100));
        }
        else if (cause == "damage")
        {
            world.QueueDamage(1, byte.MaxValue);
        }
        else
        {
            world.EnqueueInput(1, 0, new PlayerInput(InputButtons.FIRE, 64));
        }
        world.Step();
        Assert.Contains(world.Deaths, death => death.PeerId == victim);
        Assert.Equal(60, world.Players[victim].RespawnTicks);
        for (int tick = 0; tick < 60; tick++)
        {
            world.Step();
        }
        Assert.True(world.Players[victim].IsAlive);
        world.QueueDamage(victim, byte.MaxValue);
        world.Step();
        Assert.Equal(120, world.Players[victim].RespawnTicks);
    }

    [Theory]
    [InlineData(2, 1, 4, 0UL, 120)]
    [InlineData(2, 1, 4, 1UL, 180)]
    [InlineData(2, 1, 4, 2UL, 240)]
    [InlineData(2, 1, 4, ulong.MaxValue, 240)]
    [InlineData(2, 0, 4, ulong.MaxValue, 120)]
    [InlineData(0, 1, 4, 0UL, 1)]
    [InlineData(0, 1, 4, 1UL, 60)]
    [InlineData(0.25f, 0.125f, 1, 2UL, 30)]
    [InlineData(0.35f, 0, 1, 0UL, 21)]
    [InlineData(0, 0.35f, 1, 2UL, 42)]
    [InlineData(0, float.MaxValue, 4, 0UL, 1)]
    [InlineData(0, float.MaxValue, 4, ulong.MaxValue, 240)]
    [InlineData(1, 1, 1092.25f, ulong.MaxValue, ushort.MaxValue)]
    public void DelayIsCappedBeforeConversion(float baseDelay, float increase, float maximum,
        ulong previousDeaths, int expected)
    {
        EscalatingRespawnStrategy strategy = new(new EscalatingRespawnRules
        {
            BaseDelay = baseDelay,
            Increase = increase,
            MaximumDelay = maximum,
        });
        Assert.Equal(expected, strategy.DelayTicks(previousDeaths));
    }

    [Fact]
    public void DeathsSurviveRespawnsButNotRemovalOrANewMatch()
    {
        EscalatingRespawnRules rules = new() { BaseDelay = 1, Increase = 1, MaximumDelay = 3 };
        EscalatingRespawnStrategy strategy = new(rules);
        rules.BaseDelay = 10;
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
        EscalatingRespawnStrategy fresh = new(new EscalatingRespawnRules { BaseDelay = 1 });
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
