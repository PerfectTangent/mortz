using Chickensoft.AutoInject;
using Godot;
using Mortz.Client.Audio;
using Mortz.Client.Match;
using Mortz.Client.Net;
using Mortz.Client.Players;
using Mortz.Client.Views;
using Mortz.Core.Match.Configuration;
using Mortz.Protocol.Net.Lobby;
using Mortz.Protocol.Net.Roster;
using Mortz.Runtime.Tests.Net;
using Xunit;

namespace Mortz.Tests.Client;

[Collection(nameof(MortzGodotCollection))]
public class PlayerViewRosterTests : NodeServiceTest
{
    // Snapshots buffered before a mid-match leave still carry the leaver for a frame or two.
    [Fact]
    public void PlacingARetiredPlayerLeavesNoGhost()
    {
        PlayerViewManager manager = TakeManagerFromGameViewScene();
        manager.FakeDependency<INetwork>(new FakeNetwork { LocalPeerId = 1 });
        manager.FakeDependency<ISfx>(new NullSfx());
        ClientPlayers players = RegisterRuntime(new ClientPlayers(Scope));
        players.OpenMatch(new MatchConfig());
        manager.FakeDependency(FeatureLookup(players));
        HostRouted(manager);
        new RosterMsg([
            new RosterEntry(1, "Alice", 0, null, 1),
            new RosterEntry(2, "Bob", 0, null, 2),
        ], 1).Broadcast(Router);
        manager.BeginFrame();
        manager.Place(2, ViewState());
        Assert.NotNull(manager.ViewForTest(2));

        new RosterMsg([new RosterEntry(1, "Alice", 0, null, 1)], 2).Broadcast(Router);
        manager.BeginFrame();
        manager.Place(2, ViewState());
        manager.Prune();

        Assert.Null(players.Find(2));
        Assert.Throws<KeyNotFoundException>(() => manager.ViewForTest(2));

        players.CloseMatch();
        new LobbyStateMsg([new LobbyMember(1, "Alice", false, null)], []).Broadcast(Router);
        Assert.Null(players.Find(2));
        Assert.Single(players);
    }

    private static PlayerViewManager TakeManagerFromGameViewScene()
    {
        GameView shell = ResourceLoader.Load<PackedScene>(
            "res://src/Client/Match/GameView.tscn").Instantiate<GameView>();
        PlayerViewManager manager = shell.GetNode<PlayerViewManager>("Players");
        shell.RemoveChild(manager);
        shell.Free();
        return manager;
    }

    private static PlayerViewState ViewState() => new(
        Feet: new Vector2(200, 240), Aim: 0, Skin: 0, Ammo: 3, ReloadTicks: 0,
        Health: 3, RespawnTicks: 0, ParryTicks: 0, DashCooldown: 0, SpawnImmunityTicks: 0);
}
