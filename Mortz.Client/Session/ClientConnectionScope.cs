using Mortz.Client.Admin;
using Mortz.Client.Chat;
using Mortz.Client.Match;
using Mortz.Client.Players;
using Mortz.Client.Setup;
using Mortz.Client.Stats;
using Mortz.Core.Features;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Sim.Modifiers;
using Mortz.Core.Terrain;
using Mortz.Protocol.Net;

namespace Mortz.Client.Session;

public class ClientConnectionScope : IDisposable
{
    public MatchSetup Setup { get; }
    public ClientAdmin Admin { get; }
    public ClientChat Chat { get; }
    private readonly NetRouter _router;
    public ClientMatchRuntime? Match { get; private set; }
    public FeatureScope Features { get; }
    public ClientPlayers Players { get; }
    public Pings Pings { get; }
    public SessionWins Wins { get; }

    public ClientConnectionScope(NetRouter router, IClientSender sender, Func<int> localPeerId, ISessionExit sessionExit)
    {
        _router = router;
        Features = new FeatureScope("connection", router.Add, router.Remove);
        Players = Features.Register(new ClientPlayers(Features));
        Pings = Features.Register(new Pings(Features, Players));
        Wins = Features.Register(new SessionWins(Features, Players));
        Setup = Features.Register(new MatchSetup());
        Admin = Features.Register(new ClientAdmin(sender, localPeerId));
        Chat = Features.Register(new ClientChat(Admin, sessionExit, sender));
        Features.Start();
    }

    public ClientMatchRuntime OpenMatch(ClientMatchState state, TerrainMask terrain,
        MatchConfig config, MapZones zones, int localPeerId, Action<byte[]> sendInputs, Func<ulong> clock)
    {
        ObjectDisposedException.ThrowIf(Features.IsClosed, this);
        FeatureScope scope = Features.OpenChild("match");
        _router.MatchGeneration = state.Generation;
        ClientMatchRuntime match = new(scope, state, Players, terrain, config, zones, localPeerId,
            sendInputs, clock);
        scope.Own(() =>
        {
            Match = null;
            _router.MatchGeneration = -1;
        });
        scope.Start();
        Match = match;
        return match;
    }

    public void CloseMatch() => Features.CloseChild();

    public void Dispose() => Features.Dispose();
}
