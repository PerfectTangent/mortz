using Mortz.Content;
using Mortz.Core.Match.Configuration;
using Mortz.Protocol.Net;
using Mortz.Server.Content;
using Serilog;

namespace Mortz.Server.Hosting;

public readonly record struct ServerBootLoad(ServerBoot Boot, IMapSource Maps);

/// <summary>Resolves the server's boot input: CLI flags over server.toml over defaults.</summary>
public static class ServerBootLoader
{
    private const string DEFAULT_MODE_ID = "deathmatch";
    private const string SERVER_ID_FILE = "data/server-id";

    public static ServerBootLoad? TryLoad(CommandLine args, string contentRoot, string defaultMapId,
        ILogger log, ILogger contentLog)
    {
        ContentCatalogResult catalogResult = ContentCatalog.Load(contentRoot);
        ContentLog.Print(contentLog, catalogResult.Diagnostics);
        if (catalogResult.Catalog is not ContentCatalog catalog)
        {
            log.Error("content catalog is unusable");
            return null;
        }

        CatalogMapSource maps = new(catalog, contentLog);
        string mapId = args.GetValue("--map") ?? defaultMapId;
        MapSnapshot? map = maps.Load(mapId);
        if (map == null)
        {
            log.Error("failed to load map '{MapId}'", mapId);
            return null;
        }

        MatchConfig? rules = LoadRuleset(args, catalog, log, contentLog);
        ServerConfig? serverConfig = ServerConfig.Load(log);
        if (rules == null || serverConfig == null)
            return null;

        if (rules.Errors(map.Zones) is [_, ..] errors)
        {
            log.Error("cannot start on map '{MapId}': {Errors}", mapId, string.Join(" ", errors));
            return null;
        }

        string adminPassword = args.GetValue("--admin-password") ?? serverConfig.AdminPassword;
        if (adminPassword.Length > 0)
            log.Information("admin password set");
        // Query port 0 means game port + 1, resolved once the game port is bound.
        if (!TryPort(args, "--port", NetConfig.DEFAULT_PORT, 0, out int gamePort) ||
            !TryPort(args, "--query-port", 0, 1, out int queryPort))
        {
            log.Error("--port and --query-port take a UDP port number");
            return null;
        }
        bool steamPublic = serverConfig.SteamPublic || args.HasFlag("--steam-public");
        bool allowJoinInProgress = serverConfig.AllowJoinInProgress;
        if (args.HasFlag("--allow-jip"))
            allowJoinInProgress = true;
        if (args.HasFlag("--no-jip"))
            allowJoinInProgress = false;
        ServerBoot boot = new()
        {
            Map = map,
            Rules = rules,
            Catalog = catalog,
            AdminPassword = adminPassword,
            Name = ServerConfig.SanitizeName(args.GetValue("--server-name") ?? serverConfig.Name),
            GamePort = gamePort,
            QueryPort = queryPort,
            Seed = Random.Shared.Next(),
            NetStats = args.HasFlag("--net-stats"),
            AllowJoinInProgress = allowJoinInProgress,
            SteamPublic = steamPublic,
            ServerId = steamPublic ? ServerIdentity.Load(SERVER_ID_FILE, log) : "",
        };
        return new ServerBootLoad(boot, maps);
    }

    private static bool TryPort(CommandLine args, string flag, int fallback, int min, out int port)
    {
        port = fallback;
        if (!args.HasFlag(flag))
            return true;
        return int.TryParse(args.GetValue(flag), out port) && port >= min && port <= 65535;
    }

    private static MatchConfig? LoadRuleset(CommandLine args, ContentCatalog catalog, ILogger log,
        ILogger contentLog)
    {
        string? path = args.GetValue("--ruleset");
        string? modeId = args.GetValue("--mode");
        if (path != null && modeId != null)
        {
            log.Error("--ruleset and --mode are mutually exclusive");
            return null;
        }
        if (modeId != null)
            return LoadModeRules(modeId, catalog, log);
        if (path == null)
            return LoadModeRules(DEFAULT_MODE_ID, catalog, log);

        ContentReadResult<RulesetManifest> result = TomlModel.ReadFile<RulesetManifest>(path);
        ContentLog.Print(contentLog, result.Diagnostics);
        if (result.Value == null)
        {
            log.Error("failed to load ruleset '{Path}'", path);
            return null;
        }
        log.Information("ruleset '{Path}' loaded", path);
        return result.Value.ToMatchConfigSnapshot().ToMutable();
    }

    private static MatchConfig? LoadModeRules(string modeId, ContentCatalog catalog, ILogger log)
    {
        if (!catalog.TryGetMode(modeId, out ResolvedContent<GameModeManifest>? mode) || mode == null)
        {
            log.Error("unknown mode '{ModeId}'", modeId);
            return null;
        }
        log.Information("mode '{ModeId}' loaded", modeId);
        return mode.Winner.Manifest.ToMatchConfigSnapshot().ToMutable();
    }
}
