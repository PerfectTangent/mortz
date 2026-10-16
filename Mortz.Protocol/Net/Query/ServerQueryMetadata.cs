using System.Globalization;
using System.Text;
using Mortz.Protocol.Net.Names;

namespace Mortz.Protocol.Net.Query;

public static class ServerQueryMetadata
{
    public static Dictionary<string, string> ToRules(ServerInfo info) => new(StringComparer.Ordinal)
    {
        ["mortz_query"] = ServerQueryProtocol.VERSION.ToString(CultureInfo.InvariantCulture),
        ["mortz_app"] = info.AppId.ToString(CultureInfo.InvariantCulture),
        ["mortz_protocol"] = info.ProtocolVersion.ToString(CultureInfo.InvariantCulture),
        ["mortz_schema"] = info.SchemaHash.ToString("x16", CultureInfo.InvariantCulture),
        ["mortz_mode"] = SafeName.Sanitize(info.Mode, ServerQueryProtocol.MAX_TEXT_LENGTH),
        ["mortz_lobby"] = info.InLobby ? "1" : "0",
        ["mortz_join"] = info.AllowJoinInProgress ? "1" : "0",
        ["mortz_players"] = info.Players.ToString(CultureInfo.InvariantCulture)
    };

    /// <summary>Steam's game tags hold 127 bytes, comma separated.</summary>
    public const int MAX_TAG_BYTES = 127;

    // One-letter keys, so even the longest values fit.
    private static readonly (string Rule, char Tag)[] _tagKeys =
    [
        ("mortz_query", 'q'), ("mortz_app", 'a'), ("mortz_protocol", 'p'), ("mortz_schema", 's'),
        ("mortz_lobby", 'l'), ("mortz_join", 'j'), ("mortz_players", 'n'), ("mortz_mode", 'm'),
    ];

    private const char SERVER_ID_TAG = 'i';
    public const int SERVER_ID_LENGTH = 16;

    /// <summary>A Steam server's FakeIP and SteamID change with every logon, so a favorite
    /// finds it again by this ID: 16 lowercase hex digits, sent as an extra tag.</summary>
    public static bool IsServerId(string id) => id.Length == SERVER_ID_LENGTH && id.All(char.IsAsciiHexDigitLower);

    /// <summary>The rules packed into Steam game tags.</summary>
    public static string ToTags(ServerInfo info)
    {
        Dictionary<string, string> rules = ToRules(info);
        StringBuilder tags = new();
        foreach ((string rule, char tag) in _tagKeys)
        {
            if (tags.Length > 0)
                tags.Append(',');
            tags.Append(tag).Append(rules[rule]);
        }
        if (IsServerId(info.ServerId))
            tags.Append(',').Append(SERVER_ID_TAG).Append(info.ServerId);
        return tags.ToString();
    }

    public static ServerInfo ApplyTags(ServerInfo info, string tags)
    {
        Dictionary<string, string> rules = new(StringComparer.Ordinal);
        string serverId = "";
        foreach (string tag in tags.Split(','))
        {
            if (tag.Length > 0 && tag[0] == SERVER_ID_TAG && IsServerId(tag[1..]))
                serverId = tag[1..];
            else if (tag.Length > 0 && Array.FindIndex(_tagKeys, key => key.Tag == tag[0]) is int index and >= 0)
                rules[_tagKeys[index].Rule] = tag[1..];
        }
        return ApplyRules(info, rules) with { ServerId = serverId };
    }

    public static ServerInfo ApplyRules(ServerInfo info, IReadOnlyDictionary<string, string> rules)
    {
        if (!rules.TryGetValue("mortz_query", out string? format) || format != "1" ||
            !rules.TryGetValue("mortz_app", out string? appText) ||
            !uint.TryParse(appText, NumberStyles.None, CultureInfo.InvariantCulture, out uint app) ||
            !rules.TryGetValue("mortz_protocol", out string? protocolText) ||
            !int.TryParse(protocolText, NumberStyles.None, CultureInfo.InvariantCulture, out int protocol) ||
            !rules.TryGetValue("mortz_schema", out string? schemaText) || schemaText.Length != 16 ||
            !ulong.TryParse(schemaText, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong schema) ||
            !rules.TryGetValue("mortz_mode", out string? mode) || string.IsNullOrWhiteSpace(mode) ||
            !rules.TryGetValue("mortz_lobby", out string? lobby) || lobby is not ("0" or "1") ||
            !rules.TryGetValue("mortz_join", out string? join) || join is not ("0" or "1") ||
            !rules.TryGetValue("mortz_players", out string? playersText) ||
            !int.TryParse(playersText, NumberStyles.None, CultureInfo.InvariantCulture, out int players) ||
            players > info.MaxPlayers)
            return info with { MetadataValid = false };
        string safeMode = SafeName.Sanitize(mode, ServerQueryProtocol.MAX_TEXT_LENGTH);
        if (safeMode.Length == 0)
            return info with { MetadataValid = false };
        return info with
        {
            AppId = app,
            ProtocolVersion = protocol,
            SchemaHash = schema,
            Mode = safeMode,
            InLobby = lobby == "1",
            AllowJoinInProgress = join == "1",
            Players = players,
            MetadataValid = true
        };
    }
}
