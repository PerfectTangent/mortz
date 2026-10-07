using Mortz.Core.Sim;
using Mortz.Core.Terrain;
using Mortz.Protocol.Terrain;
using Mortz.Server.Hooks;

namespace Mortz.Server.Match;

/// <summary>Late-join terrain state. Records every carve until the log fills, then the bitmap is the fallback.</summary>
public sealed class TerrainHistory : IObserveMatchUpdate
{
    private readonly List<TerrainCarve> _carves = [];
    private bool _logComplete = true;

    public int CarveCount => _carves.Count;

    public void MatchUpdated(in MatchUpdate update)
    {
        foreach (Explosion explosion in update.Explosions)
        {
            Record(explosion.X, explosion.Y, explosion.Radius);
        }
    }

    public void Record(int x, int y, int radius)
    {
        if (_carves.Count >= TerrainSync.MAX_CARVES)
        {
            _logComplete = false;
            return;
        }
        _carves.Add(new TerrainCarve(
            (short)Math.Clamp(x, short.MinValue, short.MaxValue),
            (short)Math.Clamp(y, short.MinValue, short.MaxValue),
            (byte)Math.Clamp(radius, 0, byte.MaxValue)));
    }

    public TerrainSyncPayload Build(TerrainMask terrain) => _logComplete
        ? TerrainSync.Build(terrain, _carves)
        : new TerrainSyncPayload(TerrainSyncEncoding.REMOVED_BITMAP, terrain.SerializeRemoved());
}
