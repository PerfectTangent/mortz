using Chickensoft.AutoInject;
using Chickensoft.Introspection;
using Godot;
using Mortz.Client.Net;
using Mortz.Core.Features;
using Mortz.Extensions;

namespace Mortz.Client.Match;

/// <summary>Free-for-all score HUD: the local player's kills and deaths.</summary>
[Meta(typeof(IAutoNode))]
public partial class PlayerKillsHud : Control
{
    [Export] private Label _scoreLabel = null!;

    [Dependency]
    private FeatureScope Scope => this.DependOn<FeatureScope>();
    private ClientMatchState MatchState => Scope.Feature<ClientMatchState>();

    [Dependency]
    private INetwork Network => this.DependOn<INetwork>();

    public override void _Notification(int what) => this.Notify(what);

    public void OnResolved()
    {
        this.Subscribe(() => MatchState.ScoresChanged += OnScoresChanged,
            () => MatchState.ScoresChanged -= OnScoresChanged);
        Render();
    }

    // A just-swapped-out view can still get the swap's own event: skip it.
    private void Render()
    {
        if (!IsInsideTree())
            return;
        int localId = Network.LocalPeerId;
        _scoreLabel.Text =
            $"K {MatchState.Scores.Kills(localId)} / D {MatchState.Scores.Deaths(localId)}";
    }

    private void OnScoresChanged(MatchScoreSnapshot scores) => Render();
}
