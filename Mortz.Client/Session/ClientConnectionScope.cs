using Mortz.Client.Admin;
using Mortz.Client.Chat;
using Mortz.Client.Match;
using Mortz.Client.Players;
using Mortz.Client.Setup;
using Mortz.Client.Stats;
using Mortz.Core.Features;
using Mortz.Core.Match.Configuration;
using Mortz.Core.Match.Participation;
using Mortz.Core.Sim.Modifiers;
using Mortz.Core.Terrain;
using Mortz.Protocol.Net;

namespace Mortz.Client.Session;

public class ClientConnectionScope : IDisposable
{
    private readonly NetRouter _router;
    private readonly ClientPlayers _players;
    public FeatureScope Features { get; }

    public ClientConnectionScope(NetRouter router, IClientSender sender, Func<int> localPeerId, ISessionExit sessionExit)
    {
        _router = router;
        Features = new FeatureScope("connection", router.Add, router.Remove);
        _players = Features.Register(new ClientPlayers(Features));
        Features.Register(new Pings(Features, _players));
        Features.Register(new SessionWins(Features, _players));
        Features.Register(new MatchSetup());
        ClientAdmin admin = Features.Register(new ClientAdmin(Features, sender, localPeerId));
        Features.Register(new ClientChat(admin, sessionExit, sender));
        Features.Start();
    }

    public ClientMatchRuntime OpenMatch(int generation, MatchParticipation participation,
        TerrainMask terrain, MatchConfig config, MapZones zones, int localPeerId,
        Action<byte[]> sendInputs, Func<ulong> clock)
    {
        ObjectDisposedException.ThrowIf(Features.IsClosed, this);
        FeatureScope scope = Features.OpenChild("match");
        _router.MatchGeneration = generation;
        ClientMatchState state = scope.Register(new ClientMatchState(generation, participation));
        ClientMatchRuntime match = new(scope, state, _players, terrain, config, zones, localPeerId,
            sendInputs, clock);
        scope.Own(() => _router.MatchGeneration = -1);
        scope.Start();
        return match;
    }

    /// <summary>Runs the cleanup when the match closes, before its features are disposed.</summary>
    public void OwnMatch(Action cleanup) =>
        (Features.Child ?? throw new InvalidOperationException("No match is open.")).Own(cleanup);

    public void CloseMatch() => Features.CloseChild();

    public void Dispose() => Features.Dispose();
}
