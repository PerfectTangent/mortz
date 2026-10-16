using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Net;
using Mortz.Client.Players;
using Mortz.Client.Stats;
using Mortz.Core.Features;
using Mortz.Extensions;

namespace Mortz.Client.Menus;

/// <summary>Free-for-all lobby roster: one slot per player, one column.</summary>
[Meta(typeof(IAutoNode))]
public partial class SingleColumnRoster : ScrollContainer
{
    [Export] private VBoxContainer _players = null!;

    [Dependency]
    private FeatureScope Scope => this.DependOn<FeatureScope>();
    private ClientPlayers Players => Scope.Feature<ClientPlayers>();
    private Pings Pings => Scope.Feature<Pings>();
    private SessionWins Wins => Scope.Feature<SessionWins>();

    [Dependency]
    private INetwork Network => this.DependOn<INetwork>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        this.Subscribe(() => Players.Changed += Render, () => Players.Changed -= Render);
        this.Subscribe(() => Pings.Changed += Render, () => Pings.Changed -= Render);
        this.Subscribe(() => Wins.Changed += Render, () => Wins.Changed -= Render);
        Render();
    }

    // Frees immediately so same-frame re-renders never stack dying slots.
    private void Render()
    {
        if (!IsInsideTree())
            return;
        foreach (Node child in _players.GetChildren())
        {
            child.Free();
        }
        int localId = Network.LocalPeerId;
        foreach (ClientPlayer player in Players)
        {
            _players.AddChild(RosterSlots.BuildSlot(
                player, Wins.Of(player), Pings.Of(player), localId));
        }
    }
}
