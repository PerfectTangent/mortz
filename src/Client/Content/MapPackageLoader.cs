using Godot;
using Mortz.Content;

namespace Mortz.Client.Content;

public sealed record MapPackageLoadResult(
    MapPackage? Package,
    IReadOnlyList<ContentDiagnostic> Diagnostics,
    MapSourceSnapshot? Source = null)
{
    public bool HasErrors => Diagnostics.Any(d => d.Severity == ContentDiagnosticSeverity.ERROR);
}

/// <summary>Decodes a map with the engine-free <see cref="MapDecoder"/> the server uses, then
/// wraps its layers in Godot images for rendering.</summary>
public static class MapPackageLoader
{
    public static MapPackageLoadResult Load(ContentDefinition<MapManifest> definition)
    {
        MapDecodeResult result = MapDecoder.Decode(definition);
        if (result.Map is not DecodedMap map)
            return new MapPackageLoadResult(null, result.Diagnostics, result.Source);
        return new MapPackageLoadResult(new MapPackage
        {
            MapId = map.MapId,
            DisplayName = map.DisplayName,
            SuggestedPlayers = map.SuggestedPlayers,
            Hash = map.Hash,
            Width = map.Width,
            Height = map.Height,
            SpawnPoints = map.SpawnPoints,
            Zones = map.Zones,
            Background = ToImage(map.Background),
            Solid = ToImage(map.Solid),
            Destructible = ToImage(map.Destructible),
            InitialTerrain = map.InitialTerrain,
        }, result.Diagnostics, result.Source);
    }

    private static Image ToImage(MapLayer layer) =>
        Image.CreateFromData(layer.Width, layer.Height, false, Image.Format.Rgba8, layer.Rgba);
}
