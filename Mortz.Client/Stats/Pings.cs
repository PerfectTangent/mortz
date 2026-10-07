using Mortz.Client.Players;
using Mortz.Core.Features;
using Mortz.Protocol.Net;
using Mortz.Protocol.Net.Stats;

namespace Mortz.Client.Stats;

/// <summary>A player's latest ping, null until an update mentions them.</summary>
public sealed class PingSample
{
    public int? Ms { get; set; }
}

/// <summary>Applies the ping stream onto players. Each update is a full table,
/// so absent players reset to no sample.</summary>
public class Pings(FeatureScope scope, ClientPlayers players) : IHandle<PingUpdateMsg>
{
    private readonly PlayerTable<ClientPlayer, PingSample> _pings = scope.Table<ClientPlayer, PingSample>();

    public event Action? Changed;

    public int? Of(ClientPlayer player) => _pings[player].Ms;

    public void Handle(in PingUpdateMsg message)
    {
        foreach ((ClientPlayer _, PingSample sample) in _pings.Entries)
        {
            sample.Ms = null;
        }
        foreach (PeerPing ping in message.Pings)
        {
            _pings[players.GetOrCreate(ping.PeerId)].Ms = ping.PingMs;
        }
        Changed?.Invoke();
    }
}
