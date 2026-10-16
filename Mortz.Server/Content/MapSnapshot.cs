using Mortz.Content;
using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;
using Mortz.Core.Terrain;

namespace Mortz.Server.Content;

/// <summary>A map minus its PNG layers, which are dropped once the terrain mask is built.</summary>
public sealed record MapSnapshot
{
    public required string MapId { get; init; }
    public required string DisplayName { get; init; }
    public required string Hash { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required SpawnPoint[] SpawnPoints { get; init; }
    public MapZones Zones { get; init; } = MapZones.None;
    public required TerrainMask InitialTerrain { get; init; }

    public TerrainMask BuildMask() => InitialTerrain.Copy();

    public static MapSnapshot From(DecodedMap map) => new()
    {
        MapId = map.MapId,
        DisplayName = map.DisplayName,
        Hash = map.Hash,
        Width = map.Width,
        Height = map.Height,
        SpawnPoints = map.SpawnPoints,
        Zones = map.Zones,
        InitialTerrain = map.InitialTerrain,
    };
}
