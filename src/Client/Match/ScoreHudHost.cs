using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Setup;
using Mortz.Client.Ui;
using Mortz.Core.Features;
using Mortz.Core.Match.Configuration;

namespace Mortz.Client.Match;

/// <summary>Picks which score HUD is alive from the match rules.</summary>
[Meta(typeof(IAutoNode))]
public partial class ScoreHudHost : ViewVariantHost<ScoreHudKind>
{
    [Export] private PackedScene _playerKills = null!;
    [Export] private PackedScene _teamKills = null!;

    private bool _subscribed;

    [Dependency] private FeatureScope Scope => this.DependOn<FeatureScope>();
    private MatchSetup Setup => Scope.Feature<MatchSetup>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        Setup.ConfigChanged += RefreshScores;
        _subscribed = true;
        RefreshScores();
    }

    public void OnExitTree()
    {
        if (!_subscribed)
            return;
        Setup.ConfigChanged -= RefreshScores;
        _subscribed = false;
    }

    protected override ScoreHudKind CurrentKey() =>
        Setup.Config.Rules.Teams ? ScoreHudKind.TEAM_KILLS : ScoreHudKind.PLAYER_KILLS;

    private void RefreshScores()
    {
        // Without an end condition only the objective decides, so kills don't matter.
        Visible = Setup.Config.Rules.EndCondition is not NoEndConditionRules;
        if (Visible)
        {
            Refresh();
        }
    }

    protected override PackedScene SceneFor(ScoreHudKind key) =>
        key == ScoreHudKind.TEAM_KILLS ? _teamKills : _playerKills;
}
