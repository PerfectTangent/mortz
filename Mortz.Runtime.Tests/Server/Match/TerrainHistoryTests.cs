using Mortz.Core.Sim;
using Mortz.Server.Match;
using Xunit;

namespace Mortz.Runtime.Tests.Server.Match;

public class TerrainHistoryTests
{
    [Fact]
    public void RecordsEveryExplosionFromTheMatchUpdate()
    {
        TerrainHistory history = new();
        MatchUpdate update = new()
        {
            Tick = 1,
            Explosions = [new Explosion(10, 20, 4, 1, 2), new Explosion(30, 40, 8, 3, 4)],
        };

        history.MatchUpdated(update);

        Assert.Equal(2, history.CarveCount);
    }
}
