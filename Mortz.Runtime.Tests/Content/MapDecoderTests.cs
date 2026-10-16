using System.Buffers.Binary;
using Mortz.Content;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Sim;
using Mortz.Core.Sim.Modifiers;
using Xunit;

namespace Mortz.Runtime.Tests.Content;

public sealed class MapDecoderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mortz-map-decoder-{Guid.NewGuid():N}");

    private static ContentCatalog BundledCatalog()
    {
        ContentCatalogResult result = ContentCatalog.Load(Path.Combine(RepoRoot(), "content"));
        Assert.DoesNotContain(result.Diagnostics,
            diagnostic => diagnostic.Severity == ContentDiagnosticSeverity.ERROR);
        return Assert.IsType<ContentCatalog>(result.Catalog);
    }

    [Fact]
    public void DeadlockPlaysOnControlBayButNotOnAMapWithoutControlZones()
    {
        ContentCatalog catalog = BundledCatalog();
        MatchConfig config = catalog.Modes["deadlock"].Winner.Manifest.ToMatchConfigSnapshot().ToMutable();
        DecodedMap map = Assert.IsType<DecodedMap>(MapDecoder.Decode(catalog.Maps["control-bay"].Winner).Map);
        Assert.Empty(config.Errors(map.Zones));
        Assert.Contains("control", Assert.Single(config.Errors(MapZones.None)));
    }

    [Fact]
    public void EveryBundledMapDecodesAndPreservesItsSpawnPoints()
    {
        ContentCatalog catalog = BundledCatalog();
        Assert.NotEmpty(catalog.Maps);

        foreach ((string mapId, ResolvedContent<MapManifest> resolved) in catalog.Maps
                     .OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            MapDecodeResult load = MapDecoder.Decode(resolved.Winner);
            Assert.False(load.HasErrors);
            DecodedMap map = Assert.IsType<DecodedMap>(load.Map);
            Assert.Equal(mapId, map.MapId);
            Assert.True(map.Width > 0);
            Assert.True(map.Height > 0);
            Assert.Equal(4, map.Hash.Split(':').Length);
            Assert.True(map.SpawnPoints.Length >= map.SuggestedPlayers,
                $"{mapId} needs at least {map.SuggestedPlayers} spawn points");
            Assert.Equal(map.SpawnPoints.Length, map.SpawnPoints.Distinct().Count());

            IReadOnlyList<ContentDiagnostic> report = MapManifestValidator.Validate(
                resolved.Winner.Manifest, resolved.Winner.ManifestPath,
                new MapDimensions(map.Width, map.Height), ContentValidationMode.REPORT);
            Assert.Empty(report);

            SimWorld world = new(map.InitialTerrain.Copy(), new MatchConfig(), map.SpawnPoints);
            for (int slot = 1; slot <= map.SuggestedPlayers; slot++)
            {
                int peerId = 1000 + slot;
                world.AddPlayer(peerId);
                Assert.Equal(map.SpawnPoints[slot - 1].Position, world.Players[peerId].Position);
            }
        }
    }

    [Fact]
    public void LayerLargerThanAMapMayBeIsRefusedBeforeDecoding()
    {
        // A PNG with no pixel data claiming 8193x8193: refused from its header, never inflated.
        byte[] huge = PngHeader(MapDimensions.MAX_SIZE + 1, MapDimensions.MAX_SIZE + 1);
        MapDecodeResult result = MapDecoder.Decode(WriteMap(huge));

        Assert.Null(result.Map);
        Assert.True(result.HasErrors);
    }

    private ContentDefinition<MapManifest> WriteMap(byte[] png)
    {
        string mapDirectory = Path.Combine(_root, "maps", "huge");
        Directory.CreateDirectory(mapDirectory);
        foreach (string layer in new[] { "background.png", "solid.png", "destructible.png" })
        {
            File.WriteAllBytes(Path.Combine(mapDirectory, layer), png);
        }

        string manifestPath = Path.Combine(mapDirectory, "map.toml");
        File.WriteAllText(manifestPath, "name = \"Huge\"\nsuggested_players = 1\n");
        MapManifest manifest = new() { Name = "Huge", SuggestedPlayers = 1 };
        return new ContentDefinition<MapManifest>("huge", manifest, mapDirectory, manifestPath,
            new ContentPackDefinition(new ContentPackManifest("org.mortz.test", "Test", "1"), _root));
    }

    private static byte[] PngHeader(int width, int height)
    {
        byte[] ihdr = new byte[4 + 13];
        "IHDR"u8.CopyTo(ihdr);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(8), height);
        ihdr[12] = 8; // bit depth
        ihdr[13] = 6; // RGBA
        using MemoryStream png = new();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, 13);
        png.Write(word);
        png.Write(ihdr);
        BinaryPrimitives.WriteUInt32BigEndian(word, Crc32(ihdr));
        png.Write(word);
        png.Write([0, 0, 0, 0, (byte)'I', (byte)'E', (byte)'N', (byte)'D', 0xAE, 0x42, 0x60, 0x82]);
        return png.ToArray();
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
            }
        }
        return ~crc;
    }

    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Mortz.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Mortz.sln not found");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
