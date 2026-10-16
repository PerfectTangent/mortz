using Mortz.Content;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Names;
using Mortz.Server.Content;
using Serilog;

namespace Mortz.Server.Hosting;

/// <summary>Machine-local dedicated server settings.</summary>
[TomlModel]
public sealed class ServerConfig
{
    private const string FILE_NAME = "server.toml";

    public const string DEFAULT_NAME = "Mortz Server";

    /// <summary>Players who send /admin with this password get live control
    /// over the server settings in the lobby. Empty = no admin access.</summary>
    public string AdminPassword { get; set; } = "";

    public string Name { get; set; } = DEFAULT_NAME;

    public bool AllowJoinInProgress { get; set; } = true;

    /// <summary>List the server on Steam. Only the Steam build can.</summary>
    public bool SteamPublic { get; set; }

    /// <summary>Blank or all-invisible names fall back to the default.</summary>
    public static string SanitizeName(string? value)
    {
        string name = SafeName.Sanitize(value, NetConfig.MAX_SERVER_NAME_LENGTH);
        return name.Length == 0 ? DEFAULT_NAME : name;
    }

    /// <summary>Defaults when the file is missing, null when it's unusable, so a host never
    /// silently runs without their settings.</summary>
    public static ServerConfig? Load(ILogger log)
    {
        if (!File.Exists(FILE_NAME))
            return new ServerConfig();
        try
        {
            ContentReadResult<ServerConfig> result = TomlModel.Read<ServerConfig>(File.ReadAllText(FILE_NAME), FILE_NAME);
            ContentLog.Print(log, result.Diagnostics);
            ServerConfig? config = result.Value;
            if (config != null)
                log.Information("{File} loaded", FILE_NAME);
            return config;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Error(e, "failed to load {File}", FILE_NAME);
            return null;
        }
    }
}
