using Mortz.Client.Players;
using Mortz.Core.Features;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Stats;

namespace Mortz.Client.Stats;

/// <summary>A player's wins this session as last told by the server.</summary>
public sealed class WinCount
{
    public int Value { get; set; }
}

/// <summary>Applies the session-wins stream onto players. Each broadcast is a
/// full table, so absent players reset to zero.</summary>
public class SessionWins(FeatureScope scope, ClientPlayers players) : IHandle<SessionWinsMsg>
{
    private readonly PlayerTable<ClientPlayer, WinCount> _wins = scope.Table<ClientPlayer, WinCount>();

    public event Action? Changed;

    public int Of(ClientPlayer player) => _wins[player].Value;

    public void Handle(in SessionWinsMsg message)
    {
        if (!SessionStatsProtocol.TryDecode(message, out PeerWins[]? wins))
            return;
        foreach ((ClientPlayer _, WinCount count) in _wins.Entries)
        {
            count.Value = 0;
        }
        foreach (PeerWins win in wins)
        {
            _wins[players.GetOrCreate(win.PeerId)].Value = win.Wins;
        }
        Changed?.Invoke();
    }
}
