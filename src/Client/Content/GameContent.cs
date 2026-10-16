using Mortz.Client.Logging;
using Mortz.Content;
using Serilog;

namespace Mortz.Client.Content;

/// <summary>Loaded fresh each time, so content installed while the game runs shows up.</summary>
public sealed class GameContent
{
    private static readonly ILogger _log = MortzLog.For("content");

    private GameContent(ContentCatalog catalog) => Catalog = catalog;

    public ContentCatalog Catalog { get; }

    public static GameContent? Load() => Load(ContentRoot.Resolve());

    /// <summary>Null when the catalog is unusable.</summary>
    public static GameContent? Load(string contentRoot)
    {
        ContentCatalogResult result = ContentCatalog.Load(contentRoot);
        Print(result.Diagnostics);
        return result.Catalog == null ? null : new GameContent(result.Catalog);
    }

    public MapPackage? LoadMap(string mapId)
    {
        if (!Catalog.TryGetMap(mapId, out ResolvedContent<MapManifest>? resolved) || resolved == null)
        {
            _log.Error("logical map '{MapId}' was not found", mapId);
            return null;
        }
        MapPackageLoadResult result = MapPackageLoader.Load(resolved.Winner);
        Print(result.Diagnostics);
        return result.Package;
    }

    private static void Print(IReadOnlyList<ContentDiagnostic> diagnostics)
    {
        foreach (ContentDiagnostic diagnostic in diagnostics)
        {
            if (diagnostic.Severity == ContentDiagnosticSeverity.ERROR)
                _log.Error("{Diagnostic}", diagnostic);
            else
                _log.Warning("{Diagnostic}", diagnostic);
        }
    }
}
