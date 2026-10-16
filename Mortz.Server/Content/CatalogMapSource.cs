using Mortz.Content;
using Serilog;

namespace Mortz.Server.Content;

public sealed class CatalogMapSource(ContentCatalog catalog, ILogger log) : IMapSource
{
    public MapSnapshot? Load(string mapId)
    {
        if (!catalog.TryGetMap(mapId, out ResolvedContent<MapManifest>? resolved) || resolved == null)
        {
            log.Error("logical map '{MapId}' was not found", mapId);
            return null;
        }
        MapDecodeResult result = MapDecoder.Decode(resolved.Winner);
        ContentLog.Print(log, result.Diagnostics);
        return result.Map is DecodedMap map ? MapSnapshot.From(map) : null;
    }
}
