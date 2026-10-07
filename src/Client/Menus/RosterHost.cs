using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Setup;
using Mortz.Client.Ui;
using Mortz.Core.Features;
using Mortz.Extensions;

namespace Mortz.Client.Menus;

/// <summary>Picks which lobby roster layout is alive from the Teams rule.</summary>
[Meta(typeof(IAutoNode))]
public partial class RosterHost : ViewVariantHost<RosterLayout>
{
    [Export] private PackedScene _single = null!;
    [Export] private PackedScene _teamColumns = null!;

    [Dependency] private FeatureScope Scope => this.DependOn<FeatureScope>();
    private MatchSetup Setup => Scope.Feature<MatchSetup>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        this.Subscribe(() => Setup.TeamsChanged += Refresh, () => Setup.TeamsChanged -= Refresh);
        Refresh();
    }

    protected override RosterLayout CurrentKey() =>
        Setup.Config.Rules.Teams ? RosterLayout.TEAM_COLUMNS : RosterLayout.SINGLE;

    protected override PackedScene SceneFor(RosterLayout key) =>
        key == RosterLayout.TEAM_COLUMNS ? _teamColumns : _single;
}
