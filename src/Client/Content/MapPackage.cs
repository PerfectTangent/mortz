using Godot;
using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;
using Mortz.Core.Terrain;

namespace Mortz.Client.Content;

/// <summary>A decoded map with its layers as Godot images.</summary>
public sealed class MapPackage
{
    public required string MapId { get; init; }
    public required string DisplayName { get; init; }
    public required int SuggestedPlayers { get; init; }
    public required string Hash { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required SpawnPoint[] SpawnPoints { get; init; }
    public required MapZones Zones { get; init; }
    public required Image Background { get; init; }
    public required Image Solid { get; init; }
    public required Image Destructible { get; init; }
    public TerrainMask InitialTerrain { get; init; } = null!;

    public TerrainMask BuildMask() => InitialTerrain.Copy();
}
