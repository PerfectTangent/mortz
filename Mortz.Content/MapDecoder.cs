using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;
using Mortz.Core.Terrain;
using StbImageSharp;

namespace Mortz.Content;

/// <summary>One decoded PNG layer, always 8-bit RGBA.</summary>
public sealed record MapLayer(int Width, int Height, byte[] Rgba)
{
    public bool IsOpaque(int x, int y) => Rgba[(y * Width + x) * 4 + 3] >= 128;
}

/// <summary>Solid and Destructible collide, only Destructible can be carved.</summary>
public sealed class DecodedMap
{
    public required string MapId { get; init; }
    public required string DisplayName { get; init; }
    public required int SuggestedPlayers { get; init; }
    public required string Hash { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required SpawnPoint[] SpawnPoints { get; init; }
    public required MapZones Zones { get; init; }
    public required MapLayer Background { get; init; }
    public required MapLayer Solid { get; init; }
    public required MapLayer Destructible { get; init; }
    public required TerrainMask InitialTerrain { get; init; }
}

public sealed record MapDecodeResult(
    DecodedMap? Map,
    IReadOnlyList<ContentDiagnostic> Diagnostics,
    MapSourceSnapshot? Source = null)
{
    public bool HasErrors => Diagnostics.Any(d => d.Severity == ContentDiagnosticSeverity.ERROR);
}

/// <summary>Decodes map PNGs without the engine, so server and client get pixel-identical collision.
/// PNGs come from mods, so sizes are checked from the header before decompressing.</summary>
public static class MapDecoder
{
    public static MapDecodeResult Decode(ContentDefinition<MapManifest> definition)
    {
        List<ContentDiagnostic> diagnostics = [];
        ContentReadResult<MapSourceSnapshot> sourceResult = MapSourceSnapshot.Read(definition);
        diagnostics.AddRange(sourceResult.Diagnostics);
        if (sourceResult.Value is not MapSourceSnapshot source)
            return new MapDecodeResult(null, diagnostics);

        MapLayer? background = DecodePng(source.BackgroundPng, "background.png", source, diagnostics);
        MapLayer? solid = DecodePng(source.SolidPng, "solid.png", source, diagnostics);
        MapLayer? destructible = DecodePng(source.DestructiblePng, "destructible.png", source, diagnostics);
        if (background == null || solid == null || destructible == null)
            return new MapDecodeResult(null, diagnostics);
        if (!SameSize(background, solid) || !SameSize(solid, destructible))
        {
            Error(diagnostics, source.Definition.DirectoryPath,
                "background, solid and destructible layer sizes differ");
            return new MapDecodeResult(null, diagnostics);
        }

        diagnostics.AddRange(MapManifestValidator.Validate(source.Manifest,
            source.Definition.ManifestPath, new MapDimensions(solid.Width, solid.Height)));
        if (diagnostics.Any(diagnostic => diagnostic.Severity == ContentDiagnosticSeverity.ERROR))
            return new MapDecodeResult(null, diagnostics);

        return new MapDecodeResult(new DecodedMap
        {
            MapId = definition.Id,
            DisplayName = source.Manifest.Name,
            SuggestedPlayers = source.Manifest.SuggestedPlayers,
            Hash = source.CompatibilityHash,
            Width = solid.Width,
            Height = solid.Height,
            SpawnPoints =
            [
                .. source.Manifest.SpawnPoints
                    .Select(point => new SpawnPoint(new Vec2(point.X, point.Y), point.Team))
            ],
            Zones = MapZoneDefs.Compile(source.Manifest.Zones),
            Background = background,
            Solid = solid,
            Destructible = destructible,
            InitialTerrain = new TerrainMask(solid.Width, solid.Height,
                solid: solid.IsOpaque, destructible: destructible.IsOpaque),
        }, diagnostics, source);
    }

    private static MapLayer? DecodePng(ReadOnlyMemory<byte> bytes, string fileName,
        MapSourceSnapshot source, List<ContentDiagnostic> diagnostics)
    {
        string where = source.Definition.DirectoryPath;
        if (bytes.Length == 0)
        {
            Error(diagnostics, where, $"{fileName} is empty");
            return null;
        }
        try
        {
            using MemoryStream header = new(bytes.ToArray(), writable: false);
            if (ImageInfo.FromStream(header) is not { } info)
            {
                Error(diagnostics, where, $"{fileName} is not a valid PNG");
                return null;
            }
            if (info.Width is < 1 or > MapDimensions.MAX_SIZE || info.Height is < 1 or > MapDimensions.MAX_SIZE)
            {
                Error(diagnostics, where,
                    $"{fileName} is {info.Width}x{info.Height}, each side must be 1 to {MapDimensions.MAX_SIZE}");
                return null;
            }
            ImageResult image = ImageResult.FromMemory(bytes.ToArray(), ColorComponents.RedGreenBlueAlpha);
            return new MapLayer(image.Width, image.Height, image.Data);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or IndexOutOfRangeException)
        {
            Error(diagnostics, where, $"{fileName} is not a valid PNG ({e.Message})");
            return null;
        }
    }

    private static bool SameSize(MapLayer left, MapLayer right) =>
        left.Width == right.Width && left.Height == right.Height;

    private static void Error(List<ContentDiagnostic> diagnostics, string source, string message) =>
        diagnostics.Add(new ContentDiagnostic(ContentDiagnosticSeverity.ERROR, source, message));
}
