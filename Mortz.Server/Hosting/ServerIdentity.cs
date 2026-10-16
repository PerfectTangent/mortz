using System.Security.Cryptography;
using Mortz.Protocol.Net.Query;
using Serilog;

namespace Mortz.Server.Hosting;

/// <summary>The ID players' favorites find this server by on Steam. Generated once and kept in a
/// file, because the FakeIP and the anonymous SteamID both change with every logon.</summary>
public static class ServerIdentity
{
    public static string Load(string path, ILogger log)
    {
        string id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(ServerQueryMetadata.SERVER_ID_LENGTH / 2));
        try
        {
            if (File.Exists(path))
            {
                string saved = File.ReadAllText(path).Trim();
                if (ServerQueryMetadata.IsServerId(saved))
                    return saved;
                log.Warning("{Path} holds no valid server ID, replacing it", path);
            }
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, id);
            log.Information("new server ID {Id} saved to {Path}", id, path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            log.Warning("could not keep the server ID in {Path} ({Reason}), Steam favorites of this server " +
                        "won't survive a restart", path, exception.Message);
        }
        return id;
    }
}
