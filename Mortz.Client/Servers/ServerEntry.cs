using Mortz.Protocol.Net.Query;

namespace Mortz.Client.Servers;

/// <summary>One row of the browser: where it is, why it is listed, and what
/// the last probe said.</summary>
public sealed class ServerEntry(ServerEndpoint endpoint, ServerSource source, string label = "")
{
    /// <summary>Moves for a saved Steam server when it comes back under a new FakeIP.</summary>
    public ServerEndpoint Endpoint { get; set; } = endpoint;
    public bool SeenOnLan { get; set; }
    public bool SeenOnSteam { get; set; }
    public ulong SteamAccountId { get; set; }
    /// <summary>Listed under a Steam FakeIP, so reachable only through the relay.</summary>
    public bool RelayOnly { get; set; }
    /// <summary>The server's own ID from its Steam tags, how a Steam favorite finds it again.</summary>
    public string ServerId { get; set; } = "";
    public HashSet<string> ResolvedAddresses { get; } = [];
    public ServerSource Source { get; set; } = source;

    /// <summary>What the player called it. Shown until the server answers with
    /// a name of its own.</summary>
    public string Label { get; set; } = label;

    public ServerStatus Status { get; set; } = ServerStatus.UNKNOWN;
    public int PingMs { get; set; }
    public ServerInfo? Info { get; set; }

    public bool IsFavorite => Source is ServerSource.PINNED or ServerSource.FAVORITE;

    /// <summary>A relay-only server can only be found again by its ID, older servers don't send one.</summary>
    public bool CanToggleFavorite => Source != ServerSource.PINNED && (!RelayOnly || ServerId.Length > 0);

    /// <summary>Not the enum's numeric value, so reordering ServerSource
    /// cannot reorder the browser.</summary>
    public int SortRank => Source switch
    {
        ServerSource.PINNED => 0,
        ServerSource.FAVORITE => 1,
        ServerSource.LAN => 2,
        ServerSource.STEAM => 3,
        _ => 4,
    };

    public bool HasName => Info is { Name.Length: > 0 } || Label.Length > 0;

    public string DisplayName
    {
        get
        {
            if (Info is { Name.Length: > 0 } info)
                return info.Name;
            return Label.Length > 0 ? Label : Endpoint.ToString();
        }
    }
}
